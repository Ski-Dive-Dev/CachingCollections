﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace CachingCollections
{
    /// <summary>
    /// A class to help facilitate optimized queries against an in-memory collection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class uses three techniques to optimize queries:
    /// <list type="number">
    /// <item>Minimizes the number of times a collection is enumerated</item>
    /// <item>Uses caching for quick-retrieval of items</item>
    /// <item>Automatically arranges filter ordering to reduce the amount of work of a query</item>
    /// </list>
    /// </para><para>
    /// This class is for in-memory collections.  It cannot be used in situations where a LINQ provider will
    /// attempt to translate it into another language.  For example, LINQ-To-SQL does not know how to translate the
    /// methods encapsulated here into SQL (and therefore, this class should not ordinarily be used in IQueryable
    /// LINQ statements.)
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The type of the items in the collection.</typeparam>
    public partial class CachingCollectionBase<T> : ICachingCollectionCommon<T>, IEnumerable<T?>, ICloneable, IDisposable
        where T : class
    {
        // See the ItemEnumerator inner class in another file, that is also a member of this partial class.

        private const int _unknown = -1;
        protected readonly object _queryBuilderLock = new object();

        /// <summary>
        /// The collection of filters built through the fluent interface, by the client of this class.
        /// </summary>
        /// <remarks>
        /// The filters are not (necessarily) applied in the order in which they were built through the fluent
        /// interface.  Instead, since they are <a href="https://en.wikipedia.org/wiki/Commutativity">commutative
        /// </a>, they are executed in an order to minimize the amount of comparisons that need to be be made.
        /// </remarks>
        protected HashSet<Predicate<T>> _queryBuilder = new HashSet<Predicate<T>>();

        /// <summary>
        /// When a new scope is created (for example, via <see cref="StartScopedQuery"/>), this field holds the
        /// collection of filters that pre-existed the new scope.  When the scoped query goes out of scope (via a
        /// call to <see cref="Dispose()"/>), any caches created for this (scoped) query are cleared.
        /// </summary>
        protected HashSet<Predicate<T>> _preExistingQueryBuilder = new HashSet<Predicate<T>>();


        /// <summary>
        /// The collection of caches -- each one of which represents a cached collection of filtered items that one
        /// particular predicate evaluated.
        /// </summary>
        /// <remarks>
        /// This cache is maintained by its derived (or decorating) classes through the
        /// <see cref="AddFilter(Predicate{T})"/> and <see cref="RemoveFilter(Predicate{T})"/> methods.
        /// </remarks>
        // TODO: This collection is frequently sorted by qc.Items.Count -- the frequency of this sorting could
        // probably be managed-down.  For example, only when ItemEnumerator.EvalueateItemForASingleFilter() adds at
        // least one item to the Items cache of a FilterCache (i.e., sort after ItemEnumerator enumerates.)
        private ICollection<FilterCache<T>> _cache = new List<FilterCache<T>>();


        #region Constructors


        /// <summary>
        /// Use this constructor when the source collection of items has already been materialized -- this prevents
        /// a redundant enumeration of the source collection from taking place.  However, it is more efficient to
        /// use the <see cref="CachingCollectionBase{T}.CachingCollectionBase(IEnumerable{T}, bool)"/> rather than
        /// materializing the source collection just for injection into <i>this</i> constructor, which takes an
        /// <see cref="ICollection{T}"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// There are two versions of the constructor -- one which accepts a source collection of
        /// <see cref="ICollection"/> (this one), and one which accepts a source collection of
        /// <see cref="IEnumerable"/> (or <see cref="IQueryable"/>, since <see cref="IQueryable"/> inherits from
        /// <see cref="IEnumerable"/>.)
        /// </para><para>
        /// If the source collection is <i>already</i> enumerated, then use this constructor.  However, if the
        /// source collection is not (yet) enumerated, then use the constructor which accepts an
        /// <see cref="IEnumerable"/>.
        /// </para><para>
        /// Note that even though <paramref name="items"/> is an <see cref="ICollection"/>,
        /// <see cref="CachingCollectionBase{T}"/> still needs to enumerate, opportunistically, through that
        /// collection to set up its caches.  So, while <see cref="ItemsIsComplete"/> will be true, until the first
        /// enumeration, <see cref="ItemsFullyEnumerated"/> will be false.  This means (among other possible
        /// things), that any duplicates in the source items are valid items (for enumeration and counting, for
        /// example) -- regardless of the setting of <see cref="DuplicatesAlwaysRemoved"/>.
        /// </para><para>
        /// To best leverage this optimization-based behavior, perform any query that is not impacted by duplicates
        /// first -- this action will cause the caches to be filled, the source items to be fully enumerated -- and
        /// any duplicates (if <paramref name="removeDuplicates"/> is <see langword="true"/>) to be removed.
        /// </para>
        /// </remarks>
        /// <param name="items">The source items for which filtering can be applied.  The type
        /// (<typeparamref name="T"/>) of the items should ensure that <see cref="Object.GetHashCode"/> properly
        /// represents a unique item -- and importantly, that its Hash Code does not change during the item's
        /// lifetime.</param>
        /// <param name="removeDuplicates">For optimal performance via caching, duplicate source items are
        /// removed when this parameter is <see langword="true"/>.  Items are considered to be duplicates if they
        /// have the same <see cref="Object.GetHashCode"/> result and they satisfy
        /// <see cref="Object.Equals(object)"/> when compared to one another.</param>
        public CachingCollectionBase(ICollection<T> items, bool removeDuplicates = true)
        {
            _noDupesItems = items;
            ItemsIsComplete = true;

            SourceItems = items;
            DuplicatesAlwaysRemoved = removeDuplicates;
        }


        /// <summary>
        /// This is the preferred constructor to use, except when the source collection of items has already been
        /// materialized for reasons external to the construction of a <see cref="CachingCollectionBase{T}"/>
        /// object.
        /// </summary>
        /// <remarks>
        /// <para>
        /// For performance reasons, enumeration of the given <paramref name="items"/> is deferred until a method
        /// (such as <see cref="Count"/> or <see cref="ItemWithMaxValue(Func{T, int})"/> internal to
        /// <see cref="CachingCollectionBase{T}"/>) is called, or a LINQ extension method (such as <c>ToList()</c>
        /// or <c>ToArray()</c>) is called on an instance of this class.
        /// </para><para>
        /// If it were not for this enumeration deferral, then each time a new filter is added, a full (and
        /// wasteful) enumeration of the data would need to take place.
        /// </para><para>
        /// BEFORE <paramref name="items"/> is fully enumerated by this class, any changes made to that collection
        /// is reflected by the internal structures of this class.  However, once the source collection has been
        /// fully enumerated for the first time, the collection is de-coupled from the source structure, and any
        /// additions or deletions of items in the source data are no longer reflected within the
        /// <see cref="CachingCollectionBase{T}"/>.
        /// </para><para>
        /// Properties within items that are part of the collection that are arguments to
        /// <see cref="CachingCollectionBase{T}"/> queries, should not be changed by any process or code (those
        /// properties should be treated as immutable by all running code once they're set) during the lifetime of
        /// a <see cref="CachingCollectionBase{T}"/> that encompasses those items.
        /// </para><para>
        /// 
        /// The foregoing, however, does not apply to changes in the content of any enumerated/cached item.  It is
        /// important to note that if the content of one or more items changed which would affect its inclusion or
        /// exclusion from the resultant enumeration, it is not predictable as to whether those changed items will
        /// be accurately represented in enumerations of a <see cref="CachingCollectionBase{T}"/> (the caches
        /// within <see cref="CachingCollectionBase{T}"/> do not update their contents once they're set.)
        /// </para><para>
        /// Also note that before <paramref name="items"/> is fully enumerated (see
        /// <see cref="ItemsFullyEnumerated"/>, any duplicates in the source items are valid items (for enumeration
        /// and counting, for example) -- regardless of the setting of <see cref="DuplicatesAlwaysRemoved"/>.
        /// </para>
        /// </remarks>
        /// <param name="items">The source items for which filtering can be applied.  The type
        /// (<typeparamref name="T"/>) of the items should ensure that <see cref="Object.GetHashCode"/> properly
        /// represents a unique item -- and importantly, that its Hash Code does not change during the item's
        /// lifetime.</param>
        /// <param name="removeDuplicates">For optimal performance via caching, duplicate source items are
        /// removed when this parameter is <see langword="true"/>.  Items are considered to be duplicates if they
        /// have the same <see cref="Object.GetHashCode"/> result and they satisfy
        /// <see cref="Object.Equals(object)"/> when compared to one another.</param>
        public CachingCollectionBase(IEnumerable<T> items, bool removeDuplicates = true)
        {
            // Note: _items will be built while items is fully enumerated the 1st time

            SourceItems = items;
            DuplicatesAlwaysRemoved = removeDuplicates;
        }


        protected CachingCollectionBase(CachingCollectionBase<T> cachingCollection)
        {
            _noDupesItems = cachingCollection._noDupesItems;
            ItemsIsComplete = cachingCollection.ItemsIsComplete;
            _cache = cachingCollection._cache;
            SourceItems = cachingCollection.SourceItems;
            DuplicatesAlwaysRemoved = cachingCollection.DuplicatesAlwaysRemoved;
        }


        #endregion

        /// <inheritdoc/>
        public IEnumerable<T> SourceItems { get; }


        /// <inheritdoc/>
        public virtual bool Contains(T item)
        {
            _ = TryFirstTimeEnumeration();

            Debug.Assert(_noDupesItems is HashSet<T>, $"We expected {nameof(_noDupesItems)} to be a HashSet so" +
                $" that we can get O(1) performance on the Contains() call.");

            return _noDupesItems.Contains(item);
        }

        /// <inheritdoc/>
        public virtual T? ItemWithMaxValue(Func<T, int> valueGetter)
        {
            var itemWithMaxValue = TryFirstTimeEnumeration(LargerOfTwo, out var itemWithMaxValue2)
                ? _noDupesItems.Aggregate(LargerOfTwo) // _items is fully enumerated
                : itemWithMaxValue2; // the result of applying the aggregate while enumerating for first time

            return itemWithMaxValue;

            // Local function:
            T LargerOfTwo(T? best, T latest)
            {
                if (best is null) { return latest; }
                if (latest is null) { return best; }
                return valueGetter(latest) > valueGetter(best) ? latest : best;
            }
        }

        /// <inheritdoc/>
        public virtual T? ItemWithMinValue(Func<T, int> valueGetter)
        {
            var itemWithMinValue = TryFirstTimeEnumeration(SmallerOfTwo, out var itemWithMinValue2)
                ? _noDupesItems.Aggregate(SmallerOfTwo) // _items is fully enumerated
                : itemWithMinValue2; // the result of applying the aggregate while enumerating for first time

            return itemWithMinValue;

            // Local function:
            T SmallerOfTwo(T? best, T latest)
            {
                if (best is null) { return latest; }
                if (latest is null) { return best; }
                return valueGetter(latest) < valueGetter(best) ? latest : best;
            }
        }

        /// <inheritdoc/>
        public IReadOnlyCollection<T> Items { get; private set; } = new List<T>();


        /// <summary>
        /// The source of filtering when <see cref="DuplicatesAlwaysRemoved"/> is <see langword="true"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// As this field is implemented as an <see cref="HashSet{T}"/>, and is the primary source for enumeration
        /// (if <see cref="DuplicatesAlwaysRemoved"/> is <see langword="true"/>), <i>the order of the
        /// enumerated output is not predictable.</i>
        /// </para><para>
        /// This field was implemented as an <see cref="HashSet{T}"/> since it automatically removes duplicate
        /// references, which the caches also do.  Removing the duplicates from the source items is important for
        /// consistent output of pre-filtering and post-filtering enumerations via the caches.
        /// </para><para>
        /// Note that several references to <see cref="ICollection.Count"/> are made in this code, and as an
        /// <see cref="HashSet{T}"/>, that operation has fast O(1) performance.  Using an implementation of
        /// <see cref="ICollection"/> whose <see cref="ICollection.Count"/> is not O(1) would have detrimental
        /// performance implications.
        /// </para>
        /// </remarks>
        private ICollection<T> _noDupesItems = new HashSet<T>();


        /// <summary>
        /// A <see langword="bool"/> which indicates that <see cref="Items"/> represents a fully enumerated copy
        /// of the source items (<see cref="SourceItems"/>).
        /// </summary>
        /// <remarks>
        /// Does not represent whether <see cref="_noDupesItems"/> represents a fully enumerated copy of the source
        /// items -- see <see cref="ItemsFullyEnumerated"/>.
        /// </remarks>
        internal bool ItemsIsComplete { get; private set; } = false;


        /// <summary>
        /// A <see langword="bool"/> which indicates that the source items (<see cref="SourceItems"/>) has been
        /// fully enumerated at least once, and that <see cref="_noDupesItems"/> contains the full set (without
        /// duplicates) and <see cref="Items"/> contains the full set (potentially with duplicates.)
        /// </summary>
        internal bool ItemsFullyEnumerated => ItemsIsComplete && _noDupesItems.Count <= Items.Count;


        /// <inheritdoc/>
        public bool DuplicatesHaveBeenDetected => ItemsFullyEnumerated
            && _noDupesItems.Count /*the hashset*/ < Items.Count /*the collection*/;


        /// <inheritdoc/>
        public bool DuplicatesAlwaysRemoved { get; private set; }


        /// <inheritdoc/>
        public virtual int Count
        {
            get
            {
                _ = TryFirstTimeEnumeration(); // XXXXX Check if Items is full first

                return DuplicatesAlwaysRemoved ? _noDupesItems.Count : Items.Count;
            }
        }


        /// <inheritdoc/>
        public int FilteredCount
        {
            get
            {
                if (_filteredCount == _unknown)
                {
                    // First time enumeration initializes _items and Items:
                    _filteredCount = this.Count();
                }

                return _filteredCount;
            }
        }
        private int _filteredCount = _unknown;
        private bool disposedValue;


        /// <summary>
        /// If the collection has not been fully enumerated, will do so (and set <see cref="_filteredCount"/> in
        /// the process).
        /// </summary>
        /// <returns><see langword="true"/> if the source items needed to be (and were) enumerated, or
        /// <see langword="false"/> if enumeration was not required.</returns>
        private bool TryFirstTimeEnumeration()
        {
            if (!ItemsIsComplete || DuplicatesAlwaysRemoved && !ItemsFullyEnumerated)
            {
                // First time enumeration initializes _items and Items.  Calling the Count() LINQ extension method
                // on this causes an enumeration:
                _filteredCount = this.Count();

                const bool trueToIndicateFirstTimeEnumerationTookPlace = true;
                return trueToIndicateFirstTimeEnumerationTookPlace;
            }

            const bool falseToIndicateEnumerationPreviouslyDone = false;
            return falseToIndicateEnumerationPreviouslyDone;
        }

        private bool TryFirstTimeEnumeration(Func<T?, T?, T?> aggregateFunction, out T? aggregateResult)
        {
            // Note, a simpler implementation would be: aggregateResult = this.Aggregate(aggregateFunction);
            // But, that would cause a full enumeration O(n) without the benefit of getting the Count.

            aggregateResult = null;

            if (!ItemsIsComplete || DuplicatesAlwaysRemoved && !ItemsFullyEnumerated)
            {
                var count = 0;
                foreach (var item in this)
                {
                    // Even though we can't apply the aggregate function on a null item -- null item IS part of the
                    // collection and so we count it.

                    count++;
                    if (item is null) { continue; }
                    aggregateResult = aggregateFunction(aggregateResult, item);
                }
                _filteredCount = count;

                const bool trueToIndicateFirstTimeEnumerationTookPlace = true;
                return trueToIndicateFirstTimeEnumerationTookPlace;
            }

            const bool falseToIndicateEnumerationPreviouslyDone = false;
            return falseToIndicateEnumerationPreviouslyDone;
        }


        /// <summary>
        /// Adds a query condition; like adding a "Where()" clause.
        /// </summary>
        /// <param name="predicate">Example: <c>AddPredicate(p => p.IsActive)</c></param>
        protected void AddFilter(Predicate<T> predicate)
        {
            lock (_queryBuilderLock)
            {
                if (_queryBuilder.Contains(predicate))
                {
                    return;
                }

                // We don't want to call Count(), because we don't want to inadvertently enumerate SourceItems:
                var numItems = ItemsIsComplete
                    ? _noDupesItems.Count
                    : _unknown;

                var filterCache = new FilterCache<T>(predicate, numItems);
                _cache.Add(filterCache);
                _queryBuilder.Add(predicate);
            }
        }


        protected void RemoveFilter(Predicate<T> predicate)
        {
            lock (_queryBuilderLock) _queryBuilder.Remove(predicate);
        }


        /// <summary>
        /// Sets <see cref="ItemsIsComplete"/> to <see langword="true"/>.
        /// </summary>
        /// <param name="newItemCollection">If a non-null value is provided, sets <see cref="Items"/> to this
        /// collection.</param>
        /// <param name="newItemCollectionNoDupes">If a non-null value is provided, sets
        /// <see cref="_noDupesItems"/> to this collection and updates all the cached queries with its count.
        /// "No Dupes" refers to identical instances of an object, not necessarily those that are the same when
        /// compared with <see cref="IEqualityComparer"/>.</param>
        internal void SetItemsAsComplete(ICollection<T>? newItemCollection,
            ICollection<T>? newItemCollectionNoDupes)
        {
            ItemsIsComplete = true;
            if (newItemCollectionNoDupes != null)
            {
                _noDupesItems = (HashSet<T>)newItemCollectionNoDupes;

                foreach (var filterCache in _cache)
                {
                    filterCache.SetNumSourceItems(_noDupesItems.Count);
                }
            }

            if (newItemCollection != null)
            {
                Items = (IReadOnlyCollection<T>)newItemCollection;
            }
        }


        /// <inheritdoc/>>
        public IEnumerator<T?> GetEnumerator() => new ItemEnumerator(this);

        /// <inheritdoc/>>
        IEnumerator IEnumerable.GetEnumerator() => (IEnumerator)GetEnumerator();


        /// <inheritdoc/>
        protected ICachingCollection<T> StartScopedQuery()
        {
            lock (_queryBuilderLock)
            {
                // This is NOT pretty, but we can't access clone._queryBuilder directly

                var savedQueryBuilder = _queryBuilder;
                _queryBuilder = new HashSet<Predicate<T>>(_queryBuilder);
                var clone = Clone();
                _queryBuilder = savedQueryBuilder;
                return (ICachingCollection<T>)clone;
            }
        }


        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects)'
                    lock (_queryBuilderLock)
                    {
                        // Iterate through the _queryBuilder objects created in this instance and clear their caches
                        foreach (var queryBuilder in _queryBuilder)
                        {
                            var filterCache = _cache.Where(qc => qc.Predicate == queryBuilder
                                && !_preExistingQueryBuilder.Contains(qc.Predicate))
                                .FirstOrDefault();

                            _ = filterCache?.TryDisableCache();
                        }
                    }
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                disposedValue = true;
            }
        }

        // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        // ~CachingCollection()
        // {
        //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        //     Dispose(disposing: false);
        // }

        /// <inheritdoc/>>
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        /// <inheritdoc/>>
        public object Clone()
        {
            var clone = (CachingCollectionBase<T>)MemberwiseClone();
            clone._preExistingQueryBuilder = _preExistingQueryBuilder;
            clone._queryBuilder = _queryBuilder;
            return clone;
        }
    }
}
