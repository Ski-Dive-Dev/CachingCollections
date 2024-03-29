using System.Collections.Generic;
using CachingCollections;

namespace CachingCollectionBenchmark
{
    /// <summary>
    /// This example of a collection class <i>inherits</i> the Caching Collection Base class.
    /// </summary>
    /// <remarks>
    /// Using object inheritance is a little easier and less tedious than using object composition, but is more
    /// tightly coupled to the implementation of CachingCollections, and you, as the developer, have less control
    /// over what public methods from the base class are presented to the users of your class.
    /// </remarks>
    public class People : CachingCollectionBase<Person>
    {
        public People(People people) : base(people) { }

        public People(IEnumerable<Person> persons) : base(persons)
        { /* No additional construction required */ }


        public new People StartScopedQuery() // notice the use of the "new" keyword here to override base method
        {
            // Overriding the base.StartScopedQuery() is important to be able to return the ICachingCollection
            // object cast as your collections type -- so that method-chaining can be used by the user of the class.
            return (People)base.StartScopedQuery();
        }


        // A key benefit to wrapping an ordinary List of objects (like Person), is that the wrapping class -- for
        // example, this People class, can provide higher-level, easier-to-read and unit-test methods (and less
        // prone to mistakes).  "FilterByActive()" is more readable than the equivalent:
        // "MyPeople.Where(p => p.IsActive)" vs "MyPeople.FilterByActive()" (you might find better method names
        // than the ones presented here!)

        public People FilterByActive()
        {
            // Since we inherit from CachingCollectionBase, we can call its methods directly:
            RemoveFilter(nameof(FilterByActive));
            AddFilter(p => p.IsActive, nameof(FilterByActive));
            return this;
        }

        public People FilterByNotDeleted()
        {
            // Since we inherit from CachingCollectionBase, we can call its methods directly:
            AddFilter(p => !p.IsDeleted, nameof(FilterByNotDeleted));
            return this;
        }

        public People FilterByDeleted()
        {
            // Since we inherit from CachingCollectionBase, we can call its methods directly:
            AddFilter(p => p.IsDeleted, nameof(FilterByDeleted));
            return this;
        }


        public People FilterByVerySkilled()
        {
            // Since we inherit from CachingCollectionBase, we can call its methods directly:
            AddFilter(p => p.Level == SkillLevel.VeryHigh, nameof(FilterByVerySkilled));
            return this;
        }

        public People FilterByMinors()
        {
            AddFilter(p => p.Age < 18, nameof(FilterByMinors));
            return this;
        }

        public Person? GetOldestPerson() => ItemWithMaxValue(p => p.Age);

        public Person? GetYoungestPerson() => ItemWithMinValue(p => p.Age);
    }
}
