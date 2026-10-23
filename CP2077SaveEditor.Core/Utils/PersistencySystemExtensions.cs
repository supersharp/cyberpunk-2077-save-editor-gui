using System.Collections.Generic;
using System.Linq;
using WolvenKit.RED4.Save;
using WolvenKit.RED4.Types;

namespace CP2077SaveEditor.Utils
{
    public static class PersistencySystemExtensions
    {
        /// <summary>
        /// Provides an OfType&lt;T&gt; lookup over PersistencySystem2 entries when building against
        /// upstream WolvenKit (where the method is not built into the class).
        /// </summary>
        public static IEnumerable<T> OfType<T>(this PersistencySystem2 ps2) where T : RedBaseClass
        {
            if (ps2?.Entries == null)
            {
                return Enumerable.Empty<T>();
            }

            return ps2.Entries.Select(e => e.Data).OfType<T>();
        }
    }
}
