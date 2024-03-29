using System;
using System.Collections.Generic;

namespace CachingCollections
{
    /// <summary>
    /// An interface to be used by a collection class for dependency injection when an implementing collection
    /// class does not wish to subclass <see cref="CachingCollectionBase{T}"/>.
    /// </summary>
    /// <remarks>
    /// This interface exposes (2) methods that a collection class might want to hide from its clients, and
    /// <see cref="StartScopedQuery"/> -- which returns an <see cref="ICachingCollectionInternal{T}"/> object that
    /// the collection class might want to wrap and return an object of its own type so that method-chaining can be
    /// effectively used by its clients.  See the <c>PeopleDI</c> example class in the <c>UsageSamples.cs</c> file.
    /// </remarks>
    /// <typeparam name="T">The <see langword="type"/> of source items within the collection.</typeparam>
    public interface ICachingCollectionInternal<T> : ICachingCollectionCommon<T>, IEnumerable<T?>, ICloneable,
        IDisposable where T: class 
    {
        /// <summary>
        /// Adds a query condition; like adding a "Where()" clause.
        /// </summary>
        /// <param name="predicate">Example: <c>AddPredicate(p => p.IsActive)</c></param>
        void AddFilter(Predicate<T> predicate, string filterName);

        /// <summary>
        /// Removes the given query condition from the currently in-scoped query builder, if present.
        /// </summary>
        /// <remarks>
        /// It may be desirable to remove a filter before adding a contra-affective one.  For example, before
        /// adding a filter for Active items, it might be prudent to first remove any filter in effect that filters
        /// for Inactive items.
        /// <code>
        /// public PeopleDI FilterByActive()
        /// {
        ///   _cc.RemoveFilter(p => !p.IsActive);
        ///   _cc.AddFilter(p => p.IsActive);
        /// }
        /// </code>
        /// </remarks>
        /// <param name="predicate">Example: <c>RemovePredicate(p => p.IsActive)</c></param>
        void RemoveFilter(string filterName); // XXXXX void RemoveFilter(Predicate<T> predicate);

        /// <summary>
        /// Any filters, and their respective caches, that are added within the scope of this query are discarded
        /// after this query goes out-of-scope.  Any filters added to the invoked object after this scope starts
        /// are not included in this scope.
        /// </summary>
        /// <returns>A <typeparamref name="TCollection"/> object where fresh filters can be added (e.g., via
        /// method-chaining), while continuing the filtering already in place within the invoked object, and the
        /// filter caches are shared amongst all decendants of the root <typeparamref name="TCollection"/> object.
        /// </returns>
        /// <exception cref="InvalidCastException">Thrown if the invoked object cannot be cast to the given
        /// type <typeparamref name="TCollection"/>.</exception>
        ICachingCollectionInternal<T> StartScopedQuery();
    }


    /// <summary>
    /// An interface which describes the <see langword="public"/> properties and methods a collection class must
    /// implement if inheriting from the <see cref="CachingCollectionBase{T}"/> class.
    /// </summary>
    /// <remarks>
    /// At the time of this writing, only one property, <see cref="Count"/>, is required for implementation.  This
    /// commonly used property is required due to the performance implications of iterating the collection the
    /// first time and subsequent times, and the way that Caching Collection optimizes these accesses.
    /// </remarks>
    /// <typeparam name="T">The <see langword="type"/> of source items within the collection.</typeparam>
    public interface ICachingCollection<T> : IEnumerable<T?>, IDisposable where T : class
    {
        /// <summary>
        /// Returns the number of source items.  WARNING: Duplicate references affect the count.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This property returns the same value as <c>SourceItems.Count()</c>, but unlike calling the
        /// <c>Count()</c> LINQ extension method, this property will fill the caches while the
        /// <see cref="SourceItems"/> are being enumerated, if they had not previously been enumerated.
        /// </para><para>
        /// The first time this property has been accessed, if the provided source items were an
        /// <see cref="IEnumerable"/> (or <see cref="IQueryable)"/>, the source items are enumerated, making this
        /// property access <c>O(n)</c>.
        /// </para><para>
        /// Any subsequent access to this property will have <c>O(1)</c> performance.
        /// </para><para>
        /// If the source items were provided as an <see cref="IReadOnlyCollection{T}"/>, then this property will
        /// have <c>O(1)</c> performance for all accesses -- but will not cause any cache to be created or updated.
        /// </para><para>
        /// The <see cref="FilteredCount"/> property is also updated when accessing <see cref="Count"/> causes a
        /// full enumeration of of the provided <see cref="IEnumerable{T}"/> source items.
        /// </para>
        /// </remarks>
        int Count { get; }
    }


    public interface ICachingCollectionCommon<T> : ICachingCollection<T>, IEnumerable<T?>, ICloneable where T : class
    {
        /// <summary>
        /// Returns the number of source items that match the currently applied predicates.  This contrasts with
        /// the <see cref="ICachingCollection{T}.Count"/> property, which returns the number of un-filtered source
        /// items, including any duplicates.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The <see cref="Count"/> property is also updated as a result of accessing this
        /// <see cref="FilteredCount"/> property.
        /// </para><para>
        /// See the remarks for the <see cref="Count"/> property with regards to the behavior of accessing the
        /// <see cref="FilteredCount"/> and <see cref="Count"/> properties, their impact on the enumeration of
        /// the source items and their Big-O time complexity implications.
        /// </para>
        /// </remarks>
        int FilteredCount { get; }


        /// <summary>
        /// This value is injected during construction.  Duplicate items are never removed from
        /// <see cref="SourceItems"/>, which represents the collection of items provided at object construction.
        /// However, if this property is <see langword="true"/>, duplicate items are removed during any processing
        /// (such as collection enumeration or filtering) that the methods in this class perform.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Items are considered to be duplicates if they have the same <see cref="Object.GetHashCode"/> result and
        /// they satisfy <see cref="Object.Equals(object)"/> when compared to one another.
        /// </para><para>
        /// Allowing duplicate items to be removed allows the caching collections code to have better performance.
        /// </para>
        /// </remarks>
        bool DuplicatesAlwaysRemoved { get; }


        /// <summary>
        /// <see langword="true"/> if the source items contained one or more items that have the same
        /// <see cref="Object.GetHashCode"/> result and satisfy <see cref="Object.Equals(object)"/> when compared
        /// to one another.  This value is only valid if <see cref="DuplicatesAlwaysRemoved"/> and
        /// <see cref="ItemsFullyEnumerated"/> are both <see langword="true"/>.
        /// </summary>
        bool DuplicatesHaveBeenDetected { get; }


        /// <summary>
        /// Represents a materialized (fully enumerated) copy of the source items that were injected during
        /// construction.  This collection will be empty (and <see cref="ItemsIsComplete"/> will be
        /// <see langword="false"/>) if the source items is an <see cref="IEnumerable"/>, and those items haven't
        /// yet been fully enumerated.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A convenience property, allowing this class's derived class(es) and their clients access to the items
        /// that are the source data for any filtering this class performs.
        /// </para><para>
        /// If an instance of this class is constructed with an <see cref="IEnumerable{T}"/> data source,
        /// enumeration of that source is deferred until it is needed.  Once the source is enumerated, its values
        /// are placed in this property (<see cref="Items"/>) "materialized" -- meaning that future access to the
        /// items (are normally) faster than causing the source <see cref="IEnumerable{T}"/> to be repeatedly and
        /// redundantly evaluated and enumerated.
        /// </para><para>
        /// If an instance of this class is constructed using the option <c>removeDuplicateReferences: false</c>,
        /// then this property becomes the source of all filtering (after the first enumeration.)  Otherwise, if an
        /// instance of this class is constructed with the option <c>removeDuplicateReferences: true</c>, then
        /// the caches (and possibly the <see cref="_noDupesItems"/> field) become the source of all filtering for
        /// more optimal performance.
        /// </para>
        /// </remarks>
        ICollection<T> Items { get; }


        /// <summary>
        /// The source data as injected in the constructor (expressed as an <see cref="IEnumerable"/>.)
        /// </summary>
        /// <remarks>
        /// <para>
        /// Filters applied through <see cref="CachingCollectionBase{T}"/> do not effect the source data, and any
        /// duplicate items within the source data are not removed, regardless of the
        /// <see cref="DuplicatesAlwaysRemoved"/> setting.
        /// </para><para>
        /// The source for the <see cref="SourceItems"/> enumeration should always produce the same stream of
        /// items.  Once the source items have been fully enumerated (<c>ItemsFullyEnumerated</c>), any changes to
        /// the stream of source items are not reflected by the caching collection.  Also, any changes to the
        /// enumerated items may or may not be reflected by the filtering in effect.  Mutated items within a cache
        /// are not removed from the cache if that mutated item no longer satisfies the cache's predicate.
        /// Therefore, the source items should be treated as being immutable while being used in a caching
        /// collection.
        /// </para>
        /// </remarks>
        IEnumerable<T> SourceItems { get; }


        /// <summary>
        /// Returns <see langword="true"/> if the unfiltered source data contains the given
        /// <paramref name="item"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// If <see cref="ItemsFullyEnumerated"/> is <see langword="true"/>, then this method returns a very fast
        /// result (nearly O(1)) -- at least as fast as calling <c>Contains(T item)</c> directly on the source
        /// items.
        /// </para><para>
        /// However, if <see cref="ItemsFullyEnumerated"/> is <see langword="false"/>, then this method will be
        /// slightly slower than calling <c>Contains(T item)</c> directly on the source items -- however, in that
        /// case, the source items become fully enumerated ("materialized") and all future actions against this
        /// <see cref="CachingCollectionBase{T}"/> (including a subsequent call to <see cref="Contains(T)"/> --
        /// even with a different <paramref name="item"/>) are faster.
        /// </para>
        /// </remarks>
        bool Contains(T item);


        /// <summary>
        /// A fast (O(n)) means to get the item with the largest value, as determined by the given
        /// <paramref name="valueGetter"/>.
        /// </summary>
        /// <remarks>
        /// Developers often use code such as: <c>oldestPerson = people.OrderByDescending(p => p.Age).First();</c>
        /// to get the item with the largest value (in this case, the largest age.)  Using <c>OrderBy</c> is a
        /// costly, O(n*logn) operation.  Using <see cref="ItemWithMaxValue(Func{T, int})"/> is a faster O(n)
        /// operation.
        /// </remarks>
        /// <param name="valueGetter">A function that, when given an object of type <typeparamref name="T"/>,
        /// returns a value which indicates its magnitude.</param>
        /// <returns>The <typeparamref name="T"/> with the largest value.</returns>
        T? ItemWithMaxValue(Func<T, int> valueGetter);


        /// <summary>
        /// A fast (O(n)) means to get the item with the smallest value, as determined by the given
        /// <paramref name="valueGetter"/>.
        /// </summary>
        /// <remarks>
        /// Developers often use code such as: <c>youngestPerson = people.OrderBy(p => p.Age).First();</c>
        /// to get the item with the smallest value (in this case, the smallest age.)  Using <c>OrderBy</c> is a
        /// costly, O(nlogn) operation.  Using <see cref="ItemWithMinValue(Func{T, int})"/> is a faster O(n)
        /// operation.
        /// </remarks>
        /// <param name="valueGetter">A function that, when given an object of type <typeparamref name="T"/>,
        /// returns a value which indicates its magnitude.</param>
        /// <returns>The <typeparamref name="T"/> with the smallest value.</returns>
        T? ItemWithMinValue(Func<T, int> valueGetter);
    }
}