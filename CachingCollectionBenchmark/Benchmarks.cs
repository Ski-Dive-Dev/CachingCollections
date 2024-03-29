using System;
using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Attributes;
using CachingCollections;

namespace CachingCollectionBenchmark
{
    public class Benchmarks
    {
        public static IEnumerable<Person> GeneratePeople()
        {
            var i = 0;
            var rng = new Random(Seed: 12345);
            foreach (var active in new[] { true, false })
                foreach (var deleted in new[] { true, false })
                    foreach (SkillLevel skillLevel in Enum.GetValues(typeof(SkillLevel)))
                        for (var j = 0; j < 10000;  j++)
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


        [Benchmark(Baseline = true)]
        public int BenchmarkNoCaching()
        {
            // Arrange

            // The source data is injected into the CachingCollectionInternal constructor
            var items = GeneratePeople();

            // The CachingCollectionInternal object is injected into the PeopleDI constructor:
            var allPeople = items.ToList();

            var activePeople = allPeople
                .Where(p => p.IsActive)
                .ToList();

            var notDeletedPeople = allPeople
                .Where(p => !p.IsDeleted)
                .ToList();

            var activeNotDeletedPeople = allPeople
                .Where(p => p.IsActive && !p.IsDeleted)
                .ToList();

            var activeNotDeletedVerySkilledMinors = allPeople
                .Where(p => p.IsActive && !p.IsDeleted && p.Age < 18 && p.Level == SkillLevel.VeryHigh)
                .ToList();

            // We need to use the above variables so that Roslyn doesn't optimize them out:
            var nonsenseCount = activePeople.Count
                + notDeletedPeople.Count
                + activeNotDeletedPeople.Count
                + activeNotDeletedVerySkilledMinors.Count;

            return nonsenseCount;
        }


        [Benchmark]
        public int BenchmarkCachingCollectionInheritance()
        {
            // Arrange

            // The source data is injected into the CachingCollectionInternal constructor
            var items = GeneratePeople(); // We inject the source data during construction
            var allPeople = new People(items);

            using var activePeople = allPeople.StartScopedQuery()
                .FilterByActive();

            using var notDeletedPeople = allPeople.StartScopedQuery()
                .FilterByNotDeleted();

            using var activeNotDeletedPeople = activePeople.StartScopedQuery()
                .FilterByNotDeleted();

            using var activeNotDeletedVerySkilledMinors = activeNotDeletedPeople.StartScopedQuery()
                .FilterByMinors()
                .FilterByVerySkilled();


            // Act 
            var active = activePeople.ToList();
            var notDeleted = notDeletedPeople.ToList();
            var activeNotDeleted = activeNotDeletedPeople.ToList();
            var activeNotDeletedVerySkilledMinorsList = activeNotDeletedVerySkilledMinors.ToList();

            // We need to use the above variables so that Roslyn doesn't optimize them out:
            var nonsenseCount =
                active.Count
                + notDeleted.Count
                + activeNotDeleted.Count
                + activeNotDeletedVerySkilledMinorsList.Count;

            //var active = activePeople.Count;
            //var notDeleted = notDeletedPeople.Count;
            //var activeNotDeleted = activeNotDeletedPeople.Count;
            //var activeNotDeletedVerySkilledMinorsList = activeNotDeletedVerySkilledMinors.Count;

            //// We need to use the above variables so that Roslyn doesn't optimize them out:
            //var nonsenseCount =
            //    active
            //    + notDeleted
            //    + activeNotDeleted
            //    + activeNotDeletedVerySkilledMinorsList;
            return nonsenseCount;
        }

        [Benchmark]
        public int BenchmarkDependencyInjectionCachingCollection()
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

            using var activeNotDeletedVerySkilledMinors = activeNotDeletedPeople.StartScopedQuery()
                .FilterByMinors()
                .FilterByVerySkilled();


            // Act 
            var active = activePeople.ToList();
            var notDeleted = notDeletedPeople.ToList();
            var activeNotDeleted = activeNotDeletedPeople.ToList();
            var activeNotDeletedVerySkilledMinorsList = activeNotDeletedVerySkilledMinors.ToList();

            // We need to use the above variables so that Roslyn doesn't optimize them out:
            var nonsenseCount =
                active.Count
                + notDeleted.Count
                + activeNotDeleted.Count
                + activeNotDeletedVerySkilledMinorsList.Count;

            //var active = activePeople.Count;
            //var notDeleted = notDeletedPeople.Count;
            //var activeNotDeleted = activeNotDeletedPeople.Count;
            //var activeNotDeletedVerySkilledMinorsList = activeNotDeletedVerySkilledMinors.Count;

            //// We need to use the above variables so that Roslyn doesn't optimize them out:
            //var nonsenseCount =
            //    active
            //    + notDeleted
            //    + activeNotDeleted
            //    + activeNotDeletedVerySkilledMinorsList;
            return nonsenseCount;
        }
    }
}
