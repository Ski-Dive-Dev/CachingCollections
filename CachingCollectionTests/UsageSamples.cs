using System.Collections;
using CachingCollections;

namespace CachingCollectionTests
{
    public enum SkillLevel
    {
        Low,
        Medium,
        High,
        VeryHigh
    }


    public class Person
    {
        public int Id { get; init; }
        public string Name { get; init; } = String.Empty;
        public string Description { get; set; } = String.Empty;
        public int Age { get; set; }

        public bool IsActive { get; set; }
        public bool IsDeleted { get; set; }

        public SkillLevel Level { get; set; }

        // CachingCollections relies heavily on HashSet.  It's best if your "atomic" class (the type that's used
        // inside your collection) properly implements GetHashCode() and Equals() overrides.
        // In a nutshell, Use System.HashCode.Combine() on all *immutable* properties in your class (immutable
        // means at a minimum that they have no public setters, but even private setters need awareness.)
        // Two objects that should be equal MUST return the same hash code.
        // However, two objects that return the same hash code are NOT necessarily equal (due to collisions.)
        public override int GetHashCode() => System.HashCode.Combine(Id.GetHashCode(), Name.GetHashCode());
        public override bool Equals(object? obj) => obj is Person other && Id == other.Id && Name == other.Name;
    }


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
            RemoveFilter(nameof(FilterByActive)); // XXXXX RemoveFilter(p => !p.IsActive);
            AddFilter(p => p.IsActive, nameof(FilterByActive)); // XXXXX AddFilter(p => p.IsActive);
            return this;
        }

        public People FilterByNotDeleted()
        {
            // Since we inherit from CachingCollectionBase, we can call its methods directly:
            AddFilter(p => !p.IsDeleted, nameof(FilterByNotDeleted)); // XXXXX AddFilter(p => !p.IsDeleted);
            return this;
        }

        public People FilterByDeleted()
        {
            // Since we inherit from CachingCollectionBase, we can call its methods directly:
            AddFilter(p => p.IsDeleted, nameof(FilterByDeleted)); // XXXXX AddFilter(p => p.IsDeleted);
            return this;
        }


        public People FilterByVerySkilled()
        {
            // Since we inherit from CachingCollectionBase, we can call its methods directly:
            AddFilter(p => p.Level == SkillLevel.VeryHigh, nameof(FilterByActive)); // XXXXX AddFilter(p => p.Level == SkillLevel.VeryHigh);
            return this;
        }

        public Person? GetOldestPerson() => ItemWithMaxValue(p => p.Age);

        public Person? GetYoungestPerson() => ItemWithMinValue(p => p.Age);
    }


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
            _cc.AddFilter(p => p.IsDeleted, nameof(FilterByDeleted));
            return this;
        }


        public PeopleDI FilterByVerySkilled()
        {
            // All Caching Collection calls made through the injected _cc object:
            _cc.AddFilter(p => p.Level == SkillLevel.VeryHigh, nameof(FilterByVerySkilled));
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


    public class UsageSamples
    {
        public static IEnumerable<Person> GeneratePeople()
        {
            var i = 0;
            var rng = new Random(Seed: 12345);
            foreach (var active in new[] { true, false })
                foreach (var deleted in new[] { true, false })
                    foreach (SkillLevel skillLevel in Enum.GetValues(typeof(SkillLevel)))
                    {
                        var person = new Person
                        {
                            Id = i++,
                            Name = $"Person {i}",
                            Description = $"Active: {active}, Deleted: {deleted}, Skill: {skillLevel}",
                            Age = rng.Next(99),
                            IsActive = active,
                            IsDeleted = deleted,
                            Level = skillLevel
                        };
                        yield return person;
                    }
        }


        [Fact]
        public void TestInheritedCachingCollection()
        {
            // Arrange
            var items = GeneratePeople();
            var allPeople = new People(items); // We inject the source data during construction

            using var activePeople = allPeople.StartScopedQuery()
                .FilterByActive();

            // Note that because 'activePeople' was started within its own scope, and we're starting a new scope
            // on the original 'allPeople' collection, 'notDeletedPeople' will not be affected by the
            // FilterByActive() filter applied in that other scope.
            using var notDeletedPeople = allPeople.StartScopedQuery()
                .FilterByNotDeleted();

            // Note that unlike the 'notDeletedPeople' query above, this one starts a new scope on the already-
            // filtered 'activePeople' .. the result of FilterByNotDeleted() on that 'activePeople' scope yields
            // 'activeNotDeletedPeople':
            using var activeNotDeletedPeople = activePeople.StartScopedQuery()
                .FilterByNotDeleted();

            // Chaining-on to the activeNotDeletePeople, filtering by Deleted here, should not change anything:
            using var activePeople2 = activeNotDeletedPeople.StartScopedQuery()
                .FilterByDeleted();


            // Act
            var active = activePeople.ToList();
            var notDeleted = notDeletedPeople.ToList();
            var activeNotDeleted = activeNotDeletedPeople.ToList();
            var oldestPerson = allPeople.GetOldestPerson();
            var youngestPerson = allPeople.GetYoungestPerson();


            // Assert
            var expected = items.Where(p => p.IsActive);
            Assert.True(AssertSimpleCollectionsAreEquivalent(expected, active));

            expected = items.Where(p => !p.IsDeleted);
            Assert.True(AssertSimpleCollectionsAreEquivalent(expected, notDeleted));

            expected = items.Where(p => p.IsActive && !p.IsDeleted);
            Assert.True(AssertSimpleCollectionsAreEquivalent(expected, activeNotDeleted));

            var expectedOldestPerson = items.OrderByDescending(p => p.Age).FirstOrDefault();
            Assert.Equal(expectedOldestPerson, oldestPerson);

            var expectedYoungestPerson = items.OrderBy(p => p.Age).FirstOrDefault();
            Assert.Equal(expectedYoungestPerson, youngestPerson);
        }


        [Fact]
        public void TestDependencyInjectionCachingCollection()
        {
            // Arrange

            // The source data is injected into the CachingCollectionInternal constructor
            var items = GeneratePeople();
            var cachingCollection = (ICachingCollectionInternal<Person>)new CachingCollectionInternal<Person>(items);

            // The CachingCollectionInternal object is injected into the PeopleDI constructor:
            var allPeople = new PeopleDI(cachingCollection);

            using var activePeople = allPeople.StartScopedQuery()
                .FilterByActive();

            using var notDeletedPeople = allPeople.StartScopedQuery()
                .FilterByNotDeleted();

            using var activeNotDeletedPeople = activePeople.StartScopedQuery()
                .FilterByNotDeleted();

            using var activePeople2 = activeNotDeletedPeople.StartScopedQuery()
                .FilterByDeleted();


            // Act 
            var active = activePeople.ToList();
            var notDeleted = notDeletedPeople.ToList();
            var activeNotDeleted = activeNotDeletedPeople.ToList();
            var oldestPerson = allPeople.GetOldestPerson();
            var youngestPerson = allPeople.GetYoungestPerson();


            // Assert
            var expected = items.Where(p => p.IsActive);
            Assert.True(AssertSimpleCollectionsAreEquivalent(expected, active));

            expected = items.Where(p => !p.IsDeleted);
            Assert.True(AssertSimpleCollectionsAreEquivalent(expected, notDeleted));

            expected = items.Where(p => p.IsActive && !p.IsDeleted);
            Assert.True(AssertSimpleCollectionsAreEquivalent(expected, activeNotDeleted));

            var expectedOldestPerson = items.OrderByDescending(p => p.Age).FirstOrDefault();
            Assert.Equal(expectedOldestPerson, oldestPerson);

            var expectedYoungestPerson = items.OrderBy(p => p.Age).FirstOrDefault();
            Assert.Equal(expectedYoungestPerson, youngestPerson);
        }


        public static bool AssertSimpleCollectionsAreEquivalent<T>(IEnumerable<T> expected, IEnumerable<T> actual)
        {
            // TODO: This is a simplistic comparison; we're not considering duplicates nor are we testing that
            // expectedContainsAllActualValues.

            var actualHashset = new HashSet<T>(actual);
            var actualContainsAllExpectedValues = expected.All(i => actualHashset.Contains(i));
            return actualContainsAllExpectedValues;
        }
    }
}