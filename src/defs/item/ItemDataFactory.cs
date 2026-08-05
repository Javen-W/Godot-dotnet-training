using Godot;
using System;

namespace GodotDotnetTraining
{
    /// <summary>
    /// Creates mutable runtime <see cref="ItemData"/> instances from immutable <see cref="ItemDef"/> resources.
    /// Item instance generation uses this factory so authored item metadata remains in 
    /// content resources while save data stores only rolled item data.
    /// </summary>
    [GlobalClass]
    public partial class ItemDataFactory : Node
    {
        /// <summary>
        /// Creates a runtime item instance for an item ID.
        /// Authored <see cref="ItemDef"/> resources are rolled procedurally.
        /// </summary>
        /// <param name="itemID">The item identity to instantiate.</param>
        /// <param name="rng">Optional deterministic random source supplied by loot generation.</param>
        /// <returns>A mutable item instance, or <c>null</c> when no item content exists for the ID.</returns>
        public static ItemData CreateItemData(ItemID itemID, Random rng = null)
        {
            var def = ResourceRegistry.Get<ItemID, ItemDef>(itemID);
            if (def != null)
            {
                return CreateProceduralItemData(def, rng);
            }
            Logger.Warning($"Could not load ItemDef for {itemID}...");

            return null;
        }

        private static ItemData CreateProceduralItemData(ItemDef def, Random rng = null)
        {
            if (def == null)
            {
                return null;
            }

            rng ??= new Random();

            // TODO: Implement procedural item data creation.

            return new ItemData
            {
                ItemID = def.ItemID,
                Name = def.Name,
            };
        }
    }
}
