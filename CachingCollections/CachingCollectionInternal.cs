using System;
using System.Collections.Generic;

namespace CachingCollections
{
    /*
    Eric's Notes (if you're just getting started with CachingCollection, ignore these personal notes as they won't
    make sense to you!)

    When the number of hits is below a defined threshold (e.g., 50%), those items are cached.  Otherwise,
    they're enumerated and filtered through the predicate. ???

    GetActiveUsers() would be something like: GetItems(u => u.IsActive)
    var cc = new CachingCollection<User>(myUsers)
        .GetActiveUsers()
        .GetNonDeletedUsers()
        .GetUsersActiveSince(dateTime)
        .ToListAsync();

    Instead of using caching, just keep a count of the # of items a particular filter produced.  This is because
    most predicates are probably O(1)*, which is the same as retrieving from the HashSet cache -- and requires less
    memory.
    * Update: why was I thinking this?  A Contains() still needs to be executed, which is O(n) on SourceItems (Eric)

    Use a QueryBuilder which is injected into the collection.  Each of the filters within each query builder can
    be cached in CachingCollections for added performance when predicates/filters are re-used in Builders.
     */


    /// <summary>
    /// A version of CachingCollection that is used for dependency injection.  It publicly exposes some key methods
    /// that are <see langword="protected"/> in the base class.
    /// </summary>
    /// <typeparam name="T">The <see langword="type"/> of source items within the collection.</typeparam>
    public class CachingCollectionInternal<T> : CachingCollectionBase<T>, ICachingCollectionInternal<T>
         where T : class
    {
        public CachingCollectionInternal(ICollection<T> items, bool removeDuplicates = true)
            : base(items, removeDuplicates) {}

        public CachingCollectionInternal(IEnumerable<T> items, bool removeDuplicates = true)
            : base(items, removeDuplicates) {}

        protected CachingCollectionInternal(CachingCollectionBase<T> cachingCollection)
            : base(cachingCollection) {}


        /// <inheritdoc/>>
        public new void AddFilter(Predicate<T> predicate) => base.AddFilter(predicate);

        /// <inheritdoc/>>
        public new void RemoveFilter(Predicate<T> predicate) => base.RemoveFilter(predicate);

        /// <inheritdoc/>>
        public new ICachingCollectionInternal<T> StartScopedQuery()
        {
            lock (_queryBuilderLock)
            {
                // This is NOT pretty, but we can't access clone._queryBuilder directly

                var savedQueryBuilder = _queryBuilder;
                _queryBuilder = new HashSet<Predicate<T>>(_queryBuilder);
                var clone = Clone();
                _queryBuilder = savedQueryBuilder;
                return (ICachingCollectionInternal<T>)clone;
            }
        }

        public new void Dispose() => base.Dispose();
    }
}
