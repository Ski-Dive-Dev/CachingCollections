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
                lock (cachingCollection._queryBuilderLock)
                {
                    var queryBuilder = cachingCollection._queryBuilder;
                    var cache = cachingCollection._cache;

                    if (cachingCollection.ItemsIsComplete)
                    {
                        // XXXXX Maybe we don't sort if #items * #predicates is low

                        // We're assuming that IEnumerable.OrderBy() is a stable sort, and that the _cache is built
                        // in the order that the client of this class believes is most optimal, whose order we fall
                        // back on if needed (by way of OrderBy() being a stable sort):
                        var orderedActiveQueries = cache
                            .Where(qc => queryBuilder.Contains(qc.Predicate));
                            // XXXXX Experiment to remove; see HandleEndOfSourceItems()  .OrderBy(qc => qc.CacheIsComplete ? qc.Items.Count : Int32.MaxValue); // TODO: This is expensive; we should maintain _cache as an ordered collection

                        var mostRestrictiveQuery = orderedActiveQueries.First();

                        if (!mostRestrictiveQuery.MissesCacheIsDisabled && mostRestrictiveQuery.CacheIsComplete)
                        {
                            _itemEnumerator = mostRestrictiveQuery.Items.GetEnumerator();
                            _queries = orderedActiveQueries.Skip(1);
                        }
                        else
                        {
                            _itemEnumerator = cachingCollection.DuplicatesAlwaysRemoved
                                ? cachingCollection._noDupesItems.GetEnumerator()
                                : cachingCollection.Items.GetEnumerator();
                            _queries = orderedActiveQueries;
                        }
                    }
                    else
                    {
                        // We don't have a full enumeration yet, so we need to go to the SourceItems for enumeration
                        _itemEnumerator = cachingCollection.SourceItems.GetEnumerator();

                        // Try to finish off the cache that might make the biggest impact on filtering
                        _queries = cache
                            .Where(qc => queryBuilder.Contains(qc.Predicate))
                            .OrderByDescending(qc => qc.Misses?.Count ?? 0); // Most Misses == Most Restrictive

                        // Save the enumerated items in these collections:
                        if (!cachingCollection.DuplicatesAlwaysRemoved)
                        {
                            _enumeratedItems = new List<T>();
                        }
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
                if (!_itemEnumerator.MoveNext())
                {
                    HandleEndOfSourceItems();
                    const bool falseToIndicateEnumeratorIsPastEndOfCollection = false;
                    return falseToIndicateEnumeratorIsPastEndOfCollection;
                }

                // Note! if moreAvailable is false, Current will be invalid
                bool moreAvailable;
                (Current, moreAvailable) = GetNext();

                return moreAvailable;
            }


            public (T sourceItem, bool moreAvailable) GetNext()
            {
                T sourceItem;
                bool filteredIn;

                // Iterate through the source items until one passes all the filters (then return that one):
                do
                {
                    sourceItem = _itemEnumerator.Current;
                    AddItem(sourceItem);
                    filteredIn = true;

                    // Iterate through all the predicates to be applied to this item:
                    foreach (var queryCache in _queries)
                    {
                        Debug.Assert(queryCache != null, "Why would there be a null query in the cache?");
                        filteredIn &= EvaluateItemForASingleFilter(sourceItem, queryCache);
                        if (_cachingCollection.ItemsIsComplete && !filteredIn) { break; }
                        // Otherwise, even if !filteredIn, keep iterating to build the caches...
                    }
                } while (!filteredIn && _itemEnumerator.MoveNext());

                if (!filteredIn)
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
                if (queryCache.MissesCacheIsDisabled) // TODO: We need a new property that indicates whether the Hits cache is disabled
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
                else if (queryCache.Misses?.Contains(sourceItem) ?? false)
                {
                    // TODO: This additional check of the Misses cache might not be warranted if
                    // queryCache.Predicate(sourceItem) is performant.
                    queryCache.NumMisses++;
                    const bool falseToIndicateSourceItemDidNotPassFilters = false;
                    return falseToIndicateSourceItemDidNotPassFilters;
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
                    queryCache.Misses?.Add(sourceItem);

                    if (queryCache.Items.Count > 0 && queryCache.MissesCacheIsDisabled)
                    {
                        Debug.Assert(queryCache.MissesCacheIsDisabled && _cachingCollection.ItemsIsComplete,
                            "We didn't expect a cache to be disabled until source items was fully enumerated." +
                            "  That not being the case, we're inadvertently pre-maturely clearing the Misses" +
                            " cache here, which impacts the constructor where the assumption" +
                            " 'Most Misses == Most Restrictive' is relied upon when a previous first-enumeration" +
                            " was aborted (as in a '.Take(n)', where 'n' < SourceItems.Count.");

                        // Not going to use a cache because there are too many misses:
                        queryCache.Items.Clear();
                        queryCache.StopCachingMisses();
                    }

                    const bool falseToIndicateSourceItemDidNotPassFilters = false;
                    return falseToIndicateSourceItemDidNotPassFilters;
                }
            }


            private void HandleEndOfSourceItems()
            {
                lock (_cachingCollection._queryBuilderLock)
                {
                    _cachingCollection.SetItemsAsComplete(_enumeratedItems, _enumeratedItemsNoDupes);
                    _cachingCollection._cache = _cachingCollection._cache // XXXXX This is an experiment
                        .OrderBy(qc => qc.CacheIsComplete ? qc.Items.Count : Int32.MaxValue)
                        .ToList();
                }
            }

            /// <inheritdoc/>
            public void Reset() => throw new NotSupportedException();


            /// <inheritdoc/>
            public void Dispose()
            { /* Nothing to dispose */ }
        }
    }
}
