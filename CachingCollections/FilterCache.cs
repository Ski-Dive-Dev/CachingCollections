using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace CachingCollections
{
    /// <summary>
    /// A class to manage the cache of source items which match a specific predicate (i.e., boolean condition.)
    /// </summary>
    /// <typeparam name="T">The <see langword="type"/> of source items within the collection.</typeparam>
    internal class FilterCache<T> where T : class?
    {
        public string FilterName; // XXXXX
        private const int _unknown = -1;
        protected int _numItems;
        private int _maxNumMisses;
        private int _numHits;
        private int _numMisses;
        private readonly float _requiredUtilizationThreshold;

        /// <summary>
        /// Constructs a cache for the given <paramref name="predicate"/>.
        /// </summary>
        /// <param name="predicate">The function which accepts an item (of a type that's being enumerated) and
        /// a boolean expression.  The predicate returns <see langword="true"/> or <see langword="false"/>
        /// based on whether the provided item gets filtered-in or filtered-out, respectively.</param>
        /// <param name="numItems">The number of items, if known, within the source collection being
        /// enumerated.  It is used to determine whether the cache has evaluated all the source items, and is
        /// also used to determine the maximum number of permitted cache misses before the <see cref="Misses"/>
        /// cache is disabled. </param>
        /// <param name="requiredUtilizationThreshold">A ratio (value between 0 and 1, inclusively) which
        /// determines, based on the current number of items within the cache, what the maximum number of
        /// cache misses are permitted before the <see cref="Misses"/> cache becomes disabled (e.g., due to its
        /// ineffectiveness.)</param>
        public FilterCache(Predicate<T> predicate, string filterName, int numItems, float requiredUtilizationThreshold = 0.5f)
        {
            if (requiredUtilizationThreshold < 0 || requiredUtilizationThreshold > 1)
            {
                throw new ArgumentException("Value must be between 0 and 1, inclusively.",
                    nameof(requiredUtilizationThreshold));
            }

            if (numItems != _unknown && numItems < 0)
            {
                throw new ArgumentException("Value should be non-negative", nameof(numItems));
            }

            Predicate = predicate;
            FilterName = filterName; // XXXXX
            _numItems = numItems;
            _requiredUtilizationThreshold = requiredUtilizationThreshold;

            Items = numItems == _unknown
                ? new HashSet<T>()
                : new HashSet<T>(numItems);

            CalculateMaxNumMisses();
        }

        /// <summary>
        /// The boolean expression which indicates whether a given item should be filtered-in (predicate returns
        /// <see langword="true"/> or filtered-out (predicate returns <see langword="false"/>.)
        /// </summary>
        public Predicate<T> Predicate { get; }

        /// <summary>
        /// The collection of items that satisfy the predicate of this filter.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This cache of items doesn't necessarily improve performance because it's a <see cref="HashSet{T}"/> --
        /// its fast <see cref="HashSet{T}.Contains(T)"/> method is probably similar in performance to using the
        /// predicate to decide whether an item is filtered-in or not.
        /// </para><para>
        /// This collection's impact on performance comes from the fact that, when this particular filter is the
        /// most restrictive within a query scope, fewer comparisons need to be made because the collection is pre-
        /// filtered against the <see cref="CachingCollectionBase{T}.SourceItems"/>.
        /// </para>
        /// </remarks>
        public HashSet<T> Items { get; }

        [Obsolete]
        public HashSet<T>? Misses { get; private set; }

        /// <summary>
        /// The number of source items that satisfied the <see cref="Predicate"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is not a value which tracks all the hits against the cache within the cache's lifetime.  It is
        /// specifically used to track the number of source items that satisfied the <see cref="Predicate"/> after
        /// the first full enumeration of source items.
        /// </para><para>
        /// Due to potential duplicates in the source items, we cannot use the <see cref="HashSet{T}.Count"/>
        /// property of <see cref="Items"/>.  We also don't want to use that property in case at some point we
        /// decide to (programmatically) disable and clear <see cref="Items"/> for memory efficiency.
        /// </para>
        /// </remarks>
        public int NumHits
        {
            get => _numHits;
            internal set
            {
                if (value < 0)
                {
                    throw new Exception("Value must be non-negative");
                }
                _numHits = value;
            }
        }

        /// <summary>
        /// The number of source items that did not satisfy the <see cref="Predicate"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is not a value which tracks all the misses against the cache within the cache's lifetime.  It is
        /// specifically used to track the number of source items that do not satisfy the <see cref="Predicate"/>
        /// after the first full enumeration of source items.
        /// </para><para>
        /// This should equal <see cref="_numItems"/> minus <see cref="NumHits"/>, but we calculate
        /// <see cref="CacheIsComplete"/> from <see cref="NumHits"/> and <see cref="NumMisses"/>, which would
        /// result in circular logic.
        /// </para>
        /// </remarks>
        public int NumMisses
        {
            get => _numMisses;
            internal set
            {
                if (value < 0)
                {
                    throw new Exception("Value must be non-negative");
                }
                _numMisses = value;
            }
        }


        public bool CacheIsDisabled { get; private set; }

        [Obsolete]
        public bool MissesCacheIsDisabled { get; private set; }


        /// <summary>
        /// Cache is complete when all the known values in <see cref="CachingCollectionBase{T}.SourceItems"/> have
        /// been evaluated.  However, if the complete set of known values in
        /// <see cref="CachingCollectionBase{T}.SourceItems"/> is unknown, then the cache will never be "complete."
        /// </summary>
        /// <remarks>
        /// If the <see cref="CachingCollectionBase{T}.SourceItems"/> enumeration is an unbounded sequence of
        /// items, a first enumeration can never complete, and therefore, the caches can never be "complete."
        /// </remarks>
        public bool CacheIsComplete
        {
            get
            {
                Debug.Assert(_numItems != _unknown || NumMisses + NumHits != _numItems,
                    "Didn't expect NumMisses + NumHits to ever equal our code for 'unknown.'");

                return NumMisses + NumHits == _numItems;
            }
        }


        /// <summary>
        /// Disables use of the <see cref="Misses"/> cache.  This is often done due to its measured
        /// ineffectiveness, but might also be done to use less memory (at the cost of less performance.)
        /// </summary>
        [Obsolete]
        public void StopCachingMisses() => Misses = null;


        /// <summary>
        /// Once the source items has been fully enumerated (at least once), and the number of source items are
        /// known, this method can be used to tell the <see cref="FilterCache{T}"/> what that number is so that it
        /// can determine how useful the <see cref="Misses"/> cache is.
        /// </summary>
        /// <param name="numItems">The number of items (duplicates included) in the source data.</param>
        public void SetNumSourceItems(int numItems)
        {
            _numItems = numItems;
            CalculateMaxNumMisses();
            TryDisableCache();
        }

        /// <summary>
        /// Checks if the <see cref="Misses"/> cache has been effective (based on the injected
        /// <see cref="_requiredUtilizationThreshold"/>), and disables it if it has not been.
        /// </summary>
        /// <remarks>
        /// The naming of this method follows the
        /// <a href="https://medium.com/@lexitrainerph/understanding-the-c-tryxxx-method-pattern-from-basic-to-advanced-b43a895d4cd4">
        /// Try Method Pattern</a> -- it's not intended to convey an impression that it's desirable to disable the
        /// cache.
        /// </remarks>
        /// <returns><see langword="true"/> or <see langword="false"/> if the cache is currently DISabled, or
        /// ENabled, respectively.</returns>
        internal bool TryDisableCache()
        {
            MissesCacheIsDisabled = NumMisses > _maxNumMisses && _numItems != _unknown;

            if (MissesCacheIsDisabled)
            {
                Items.Clear();
                StopCachingMisses();
            }

            return MissesCacheIsDisabled;
        }


        /// <summary>
        /// Sets <see cref="_maxNumMisses"/> based on the injected <see cref="_requiredUtilizationThreshold"/> and
        /// the <see cref="_numItems"/> (which can be set via <see cref="SetNumSourceItems(int)"/>), or a negative
        /// value if it cannot currently be calculated.
        /// </summary>
        private void CalculateMaxNumMisses() =>
            _maxNumMisses = (int)Math.Ceiling(_numItems * _requiredUtilizationThreshold);
    }
}
