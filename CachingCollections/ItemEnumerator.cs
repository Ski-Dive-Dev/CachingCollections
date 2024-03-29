using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;

namespace CachingCollections
{
    /// <inheritdoc/>
    public partial class CachingCollectionBase<T> where T : class
    {
        /// <summary>
        /// An IEnumerator which builds internal caches as the source items are enumerated.
        /// </summary>
        class ItemEnumerator : IEnumerator<T?>
        {
            private readonly ICollection<T>? _enumeratedItems;
            private readonly ICollection<T>? _enumeratedItemsNoDupes;
            private readonly IEnumerator<T> _itemEnumerator;
            private readonly IEnumerable<FilterCache<T>> _queries;
            private readonly CachingCollectionBase<T> _cachingCollection;

            // If we have at least one completed cache, enumerate over the smallest one (note: we can't have a
            //   completed cache without having a fully enumerated source)
            // If we have an enumerated items list, enumerate over that
            // Otherwise, enumerate over the injected collection (IEnumerable, IQueryable) -- and build the items list

            public ItemEnumerator(CachingCollectionBase<T> cachingCollection)
            {
                _cachingCollection = cachingCollection;
                _ = cachingCollection.TryOptimizeQueryOrder();

                lock (cachingCollection._queryBuilderLock)
                {
                    var orderedActiveQueries = cachingCollection.GetQueriesWithinScope_RequiresLock();

                    if (cachingCollection.ItemsIsComplete)
                    {
                        var mostRestrictiveQuery = orderedActiveQueries.First();

                        var canUseEnabledCompletedCache = !mostRestrictiveQuery.CacheIsDisabled
                            && mostRestrictiveQuery.CacheIsComplete;

                        if (canUseEnabledCompletedCache)
                        {
                            _itemEnumerator = mostRestrictiveQuery.Items.GetEnumerator();
                            _queries = orderedActiveQueries.Skip(1);
                        }
                        else
                        {
                            _itemEnumerator = cachingCollection.DuplicatesAlwaysRemoved
                                ? cachingCollection.NoDupeItems.GetEnumerator()
                                : cachingCollection.Items.GetEnumerator();
                            _queries = orderedActiveQueries;
                        }

                        Debug.Assert(_enumeratedItems is null, $"Since" +
                            $" {nameof(cachingCollection.ItemsIsComplete)}, we would expect" +
                            $" {nameof(_enumeratedItems)} to be null; otherwise" +
                            $" {nameof(cachingCollection.Items)} will get overwritten.");

                        Debug.Assert(_enumeratedItemsNoDupes is null, $"Since" +
                            $" {nameof(cachingCollection.ItemsIsComplete)}, we would expect" +
                            $" {nameof(_enumeratedItemsNoDupes)} to be null; otherwise" +
                            $" {nameof(cachingCollection.NoDupeItems)} will get overwritten.");
                    }
                    else
                    {
                        // We were provided an IEnumerable (instead of an ICollection), and we don't have a full
                        // enumeration of source items yet, so we need to go to the SourceItems for enumeration.
                        // Note that any changes client makes to SourceItems are reflected here, but once
                        // ItemsIsComplete enumerations are not affected by any changes client makes to SourceItems.
                        _itemEnumerator = cachingCollection.SourceItems.GetEnumerator();

                        // Note: we might enter this 'else' clause more then once if the client never consumed the
                        // full enumeration in a foreach (calls like .ToList() would normally fully enumerate.)

                        _queries = orderedActiveQueries;


                        // Save the enumerated items in these collections:

                        // If enumeration completes, this assigns to cachingCollection.Items:
                        _enumeratedItems = new List<T>();

                        // If enumeration completes, this assigns to cachingCollection._noDupeItems
                        _enumeratedItemsNoDupes = new HashSet<T>();
                    }
                }
            }


            /// <inheritdoc/>
            object? IEnumerator.Current => Current;

            /// <inheritdoc/>
            public T? Current { get; private set; } = default;


            /// <inheritdoc/>
            public bool MoveNext()
            {
                // Note! if moreAvailable is false, Current will be invalid
                bool moreAvailable;
                (Current, moreAvailable) = GetNext();

                return moreAvailable;
            }


            public (T sourceItem, bool moreAvailable) GetNext()
            {
                T sourceItem = _itemEnumerator.Current; // hack to get around not being able to use default(T)
                var filteredIn = false;
                var stillSourceItemsLeftToEnumerate = true;

                // Note: _itemEnumerator may have already been pre-filtered using the most restrictive predicate.

                // Iterate through the source items until one passes all the filters (then return that one):
                while (!filteredIn && (stillSourceItemsLeftToEnumerate = _itemEnumerator.MoveNext()))
                {
                    sourceItem = _itemEnumerator.Current;
                    AddItem(sourceItem);
                    filteredIn = true;

                    // Iterate through all the (remaining) predicates to be applied to this item:
                    foreach (var queryCache in _queries)
                    {
                        Debug.Assert(queryCache != null, "Why would there be a null query in the cache?");
                        filteredIn &= EvaluateItemForASingleFilter(sourceItem, queryCache);
                        if (_cachingCollection.ItemsIsComplete && !filteredIn) { break; }
                        // Otherwise, even if !filteredIn, keep iterating to build the caches...
                    }
                }

                if (!stillSourceItemsLeftToEnumerate)
                {
                    // MoveNext() returned false -- we're at the end of the source enumeration
                    HandleEndOfSourceItems();

                    // NOTE: sourceItem is undefined here (it's invalid), but we can't return default for T
                    return (sourceItem, false);
                }

                return (sourceItem, true);


                // Local function:
                void AddItem(T item)
                {
                    _enumeratedItems?.Add(item);
                    _enumeratedItemsNoDupes?.Add(item);
                }
            }


            internal bool EvaluateItemForASingleFilter(T sourceItem, FilterCache<T> queryCache)
            {
                // Bear in mind that the sourceItem isn't some random item provided by random client code -- it's
                // an item from the finite set of SourceItems -- after the first enumeration, an active cache will
                // contain the definitive answer as to whether the item meets that cache's predicate.

                if (queryCache.CacheIsDisabled)
                {
                    // Not trying to cache these items (any more)
                    return queryCache.Predicate(sourceItem);
                }

                else if (queryCache.Items.Contains(sourceItem))
                {
                    // It was previously cached, so filteredIn is still true
                    queryCache.NumHits++;
                    const bool trueBecauseItemWasPreviouslyEvaluatedAndIsInCache = true;
                    return trueBecauseItemWasPreviouslyEvaluatedAndIsInCache;
                }
                else if (queryCache.Predicate(sourceItem))
                {
                    // It passes the condition, so add it to the cache, and filteredIn is still true
                    queryCache.NumHits++;
                    queryCache.Items.Add(sourceItem);
                    const bool trueBecauseItemPassedPredicate = true;
                    return trueBecauseItemPassedPredicate;
                }
                else
                {
                    queryCache.NumMisses++;
                    const bool falseToIndicateSourceItemDidNotPassFilters = false;
                    return falseToIndicateSourceItemDidNotPassFilters;
                }
            }


            private void HandleEndOfSourceItems() =>
                _cachingCollection.HandleEndOfSourceItems(_enumeratedItems, _enumeratedItemsNoDupes);


            /// <inheritdoc/>
            public void Reset() => throw new NotSupportedException();


            /// <inheritdoc/>
            public void Dispose()
            { /* Nothing to dispose */ }
        }
    }
}
