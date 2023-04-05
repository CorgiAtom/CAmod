#region Copyright & License Information
/*
 * Copyright 2007-2020 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.CA.Traits
{
	[Desc("Lists actors this actor may be upgraded to (to replace in production queue).")]
	public class ProvidesUpgradeInfo : ConditionalTraitInfo
	{
		[Desc("Type.")]
		public readonly string Type = null;

		public override object Create(ActorInitializer init) { return new ProvidesUpgrade(init.Self, this); }
	}

	public class ProvidesUpgrade : ConditionalTrait<ProvidesUpgradeInfo>, INotifyCreated
	{
		public readonly new ProvidesUpgradeInfo Info;
		readonly string type;
		UpgradesManager upgradesManager;

		public ProvidesUpgrade(Actor self, ProvidesUpgradeInfo info)
			: base(info)
		{
			Info = info;
			upgradesManager = self.Owner.PlayerActor.Trait<UpgradesManager>();
			type = Info.Type;

			if (string.IsNullOrEmpty(type))
				type = self.Info.Name;
		}

		void INotifyCreated.Created(Actor self)
		{
			if (IsTraitDisabled)
				return;

			upgradesManager.UpgradeProviderCreated(type);
		}

		protected override void TraitEnabled(Actor self)
		{
			upgradesManager.UpgradeProviderCreated(type);
		}
	}
}
