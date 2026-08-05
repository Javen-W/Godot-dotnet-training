using Godot;
using System.Runtime.Serialization;

namespace GodotDotnetTraining
{
	/// <summary>
	/// Base serializable runtime class for all item instances.
	/// Stores mutable per-instance state while keeping a stable <see cref="ItemID"/>
	/// reference back to the authored static resource.
	/// </summary>
	[GlobalClass]
	[DataContract]
	public partial class ItemData : Resource
	{
		/// <summary>
		/// Which ItemID this instance is keyed to in ResourceRegistry.
		/// Used to identify item type and look up the preset template.
		/// </summary>
		[Export]
		[DataMember]
		public ItemID ItemID { get; set; }

		/// <summary>
		/// Optional per-instance display name override.
		/// When blank, UI should fall back to the authored <see cref="ItemDef.Name"/>.
		/// </summary>
		[Export]
		[DataMember]
		public string Name { get; set; }

		#region Getters

		/// <summary>
		/// The non-serializable static resources of this item instance.
		/// Uses ItemID to populate the ItemDef resource from the ResourceRegistry.
		/// </summary>
		public ItemDef ItemDef => ResourceRegistry.Get<ItemID, ItemDef>(ItemID);

		/// <summary>Display name resolved from the instance override or its shared item definition.</summary>
		public string DisplayName => string.IsNullOrWhiteSpace(Name) ? ItemDef?.Name ?? "" : Name;

		public virtual Texture2D Icon => ItemDef?.Icon;
		public virtual PackedScene EquippedModel => ItemDef?.EquippedModel;
		public virtual PackedScene DropModel => ItemDef?.DropModel;

		# endregion

		#region Constructors

		// Make sure you provide a parameterless constructor.
		public ItemData() : this(ItemID.NULL) { }

		public ItemData(ItemID itemID)
		{
			ItemID = itemID;
			Name = "";
		}

		#endregion
	}
}
