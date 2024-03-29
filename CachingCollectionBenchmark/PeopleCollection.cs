using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace CachingCollectionBenchmark
{
    public class PeopleCollection : IEnumerable<Person>
    {
        private readonly ICollection<Person> _cc;

        public int Count => _cc.Count;

        public PeopleCollection(ICollection<Person> cc)
        {
            _cc = cc;
        }

        public ICollection<Person> FilterByActive()
        {
            return _cc.Where(p => p.IsActive).ToList();
        }

        public ICollection<Person> FilterByNotDeleted()
        {
            return _cc.Where(p => !p.IsDeleted).ToList();
        }

        public ICollection<Person> FilterByDeleted()
        {
            return _cc.Where(p => p.IsDeleted).ToList();
        }


        public ICollection<Person> FilterByVerySkilled()
        {
            return _cc.Where(p => p.Level == SkillLevel.VeryHigh).ToList();
        }

        public ICollection<Person> FilterByMinors()
        {
            return _cc.Where(p => p.Age < 18).ToList();
        }

        public Person GetOldestPerson() => _cc.OrderByDescending(p => p.Age).First();

        public Person GetYoungestPerson() => _cc.OrderBy(p => p.Age).First();


        public IEnumerator<Person?> GetEnumerator() => _cc.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _cc.GetEnumerator();
    }
}
