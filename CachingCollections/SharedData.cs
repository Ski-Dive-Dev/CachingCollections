using System.Collections.Generic;
using System.Linq;

namespace CachingCollections
{
    internal class SharedData<T> where T : class
    {
        public IEnumerable<T> SourceItems { get; set; } = Enumerable.Empty<T>();
        public bool ItemsIsComplete { get; set; }
        public ICollection<T> Items { get; set; } = new List<T>();
        public bool DuplicatesAlwaysRemoved { get; set; }
        public ICollection<T> NoDupeItems { get; set; } = new HashSet<T>();
    }
}
