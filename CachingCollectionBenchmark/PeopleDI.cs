using System.Collections;
using System.Collections.Generic;
using CachingCollections;

namespace CachingCollectionBenchmark
{
    /// <summary>
    /// This example of a collection class using <i>object composition</i> by injecting a caching collection, and
    /// uses that injected <see cref="ICachingCollection{T}"/> for all calls.
    /// </summary>
    /// <remarks>
    /// It's a little more work to use object composition, but gives you more control over the methods that are
    /// exposed to the class's clients.
    /// </remarks>
    public class PeopleDI : ICachingCollection<Person>
    {
        private readonly ICachingCollectionInternal<Person> _cc;

        // With object composition, methods and properties available through the injected ICachingCollection
        // needs to be explicitly made available to the users of this PeopleDI class -- giving you, the developer
        // of the collections class more control over what users of your class are able to do.  For example,
        // exposing the Count property:
        public int Count => _cc.Count;

        public PeopleDI(ICachingCollectionInternal<Person> cc) // Injecting the caching collection
        {
            _cc = cc;
        }

        // Since we're not inheriting from the base class in this DI example, there is no base method to override,
        // and therefore, unlike in the "inheritance" example, we don't need to use the "new" keyword here:
        public PeopleDI StartScopedQuery()
        {
            return new PeopleDI(_cc.StartScopedQuery());
        }

        public PeopleDI FilterByActive()
        {
            // All Caching Collection calls made through the injected _cc object:
            _cc.RemoveFilter(nameof(FilterByActive));
            _cc.AddFilter(p => p.IsActive, nameof(FilterByActive));
            return this;
        }

        public PeopleDI FilterByNotDeleted()
        {
            // All Caching Collection calls made through the injected _cc object:
            _cc.AddFilter(p => !p.IsDeleted, nameof(FilterByNotDeleted));
            return this;
        }

        public PeopleDI FilterByDeleted()
        {
            // All Caching Collection calls made through the injected _cc object:
            _cc.AddFilter(p => p.IsDeleted,nameof(FilterByDeleted));
            return this;
        }


        public PeopleDI FilterByVerySkilled()
        {
            // All Caching Collection calls made through the injected _cc object:
            _cc.AddFilter(p => p.Level == SkillLevel.VeryHigh, nameof(FilterByVerySkilled));
            return this;
        }

        public PeopleDI FilterByMinors()
        {
            _cc.AddFilter(p => p.Age < 18,nameof(FilterByMinors));
            return this;
        }

        public Person? GetOldestPerson() => _cc.ItemWithMaxValue(p => p.Age);

        public Person? GetYoungestPerson() => _cc.ItemWithMinValue(p => p.Age);


        public IEnumerator<Person?> GetEnumerator() => _cc.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _cc.GetEnumerator();


        // Note: If your collections class uses DI (like this one does), it's important to call the Dispose()
        // method on the injected collections class like this:
        public void Dispose() => _cc.Dispose();
    }
}
