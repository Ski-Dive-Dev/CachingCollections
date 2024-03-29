using System;

namespace CachingCollectionBenchmark
{
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
}
