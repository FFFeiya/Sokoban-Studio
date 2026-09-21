using System.Collections.Generic;
using UnityEngine;

namespace Sokoban
{
    /// <summary>
    /// Ordered list of the shipped levels. Runtime code must never mutate entries of this catalog.
    /// It lives in its own file (matching Unity's one-asset-type-per-script requirement) so the
    /// catalog asset gets a valid script reference.
    /// </summary>
    [CreateAssetMenu(menuName = "Sokoban/Level Catalog")]
    public class LevelCatalog : ScriptableObject
    {
        public List<LevelDefinition> levels = new List<LevelDefinition>();
    }
}
