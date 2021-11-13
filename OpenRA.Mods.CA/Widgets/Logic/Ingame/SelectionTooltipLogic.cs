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

using System;
using System.Linq;
using OpenRA.Mods.CA.Traits;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Primitives;
using OpenRA.Widgets;

namespace OpenRA.Mods.CA.Widgets.Logic
{
	public class SelectionTooltipLogic : ChromeLogic
	{
		readonly World world;
		int selectionHash;
		readonly Widget widget;
		int originalDescLabelHeight;
		int iconMargin;

		[ObjectCreator.UseCtor]
		public SelectionTooltipLogic(Widget widget, World world)
		{
			this.world = world;
			this.widget = widget;
			widget.IsVisible = () => Game.Settings.Game.SelectionTooltip && !Game.Settings.Debug.PerfText;
			originalDescLabelHeight = -1;
			iconMargin = -1;
		}

		public override void Tick()
		{
			if (selectionHash == world.Selection.Hash)
				return;

			UpdateTooltip();
			selectionHash = world.Selection.Hash;
		}

		void UpdateTooltip()
		{
			if (world.Selection.Actors.Count() != 1)
			{
				widget.Bounds.X = Game.Renderer.Resolution.Width;
				widget.Bounds.Y = Game.Renderer.Resolution.Height;
				return;
			}

			var mapRules = world.Map.Rules;
			var nameLabel = widget.Get<LabelWidget>("NAME");
			var armorTypeLabel = widget.Get<LabelWidget>("ARMORTYPE");
			var armorTypeIcon = widget.Get<ImageWidget>("ARMORTYPE_ICON");
			var descLabel = widget.Get<LabelWidget>("DESC");
			var strengthsLabel = widget.Get<LabelWidget>("STRENGTHS");
			var weaknessesLabel = widget.Get<LabelWidget>("WEAKNESSES");
			var attributesLabel = widget.Get<LabelWidget>("ATTRIBUTES");
			var font = Game.Renderer.Fonts[nameLabel.Font];
			var descFont = Game.Renderer.Fonts[descLabel.Font];
			var formatBuildTime = new CachedTransform<int, string>(time => WidgetUtils.FormatTime(time, world.Timestep));

			if (originalDescLabelHeight == -1)
				originalDescLabelHeight = descLabel.Bounds.Height;

			if (iconMargin == -1)
				iconMargin = armorTypeIcon.Bounds.X;

			descLabel.Bounds.Height = originalDescLabelHeight;

			var descLabelPadding = descLabel.Bounds.Height;

			var actor = world.Selection.Actors.First().Info;
			if (actor == null)
				return;

			var tooltip = actor.TraitInfos<TooltipInfo>().FirstOrDefault(info => info.EnabledByDefault);
			var name = tooltip != null ? tooltip.Name : actor.Name;

			var cost = 0;
			var valued = actor.TraitInfoOrDefault<ValuedInfo>();
			if (valued != null)
				cost = valued.Cost;

			nameLabel.Text = name;

			var nameSize = font.Measure(name);

			armorTypeLabel = GetArmorTypeLabel(armorTypeLabel, actor);

			var tooltipExtras = actor.TraitInfoOrDefault<TooltipExtrasInfo>();
			if (tooltipExtras != null)
			{
				strengthsLabel.Text = tooltipExtras.Strengths.Replace("\\n", "\n");
				weaknessesLabel.Text = tooltipExtras.Weaknesses.Replace("\\n", "\n");
				attributesLabel.Text = tooltipExtras.Attributes.Replace("\\n", "\n");
			}
			else
			{
				strengthsLabel.Text = "";
				weaknessesLabel.Text = "";
				attributesLabel.Text = "";
			}

			var armorTypeSize = armorTypeLabel.Text != "" ? font.Measure(armorTypeLabel.Text) : new int2(0, 0);
			armorTypeIcon.Visible = armorTypeSize.Y > 0;
			armorTypeLabel.Bounds.Y = armorTypeIcon.Bounds.Y;

			var extrasSpacing = descLabel.Bounds.X / 2;

			var buildable = actor.TraitInfoOrDefault<BuildableInfo>();
			var descSize = new int2(0, 0);

			if (buildable != null)
			{
				descLabel.Text = buildable.Description.Replace("\\n", "\n");
				descSize = descFont.Measure(descLabel.Text);
			}
			else
			{
				descLabel.Text = "";
			}

			descLabel.Bounds.Width = descSize.X;
			descLabel.Bounds.Height = descSize.Y;

			var strengthsSize = strengthsLabel.Text != "" ? descFont.Measure(strengthsLabel.Text) : new int2(0, 0);
			var weaknessesSize = weaknessesLabel.Text != "" ? descFont.Measure(weaknessesLabel.Text) : new int2(0, 0);
			var attributesSize = attributesLabel.Text != "" ? descFont.Measure(attributesLabel.Text) : new int2(0, 0);

			strengthsLabel.Bounds.Y = descLabel.Bounds.Bottom + extrasSpacing;
			weaknessesLabel.Bounds.Y = descLabel.Bounds.Bottom + strengthsSize.Y + extrasSpacing;
			attributesLabel.Bounds.Y = descLabel.Bounds.Bottom + strengthsSize.Y + weaknessesSize.Y + extrasSpacing;

			descLabel.Bounds.Height += strengthsSize.Y + weaknessesSize.Y + attributesSize.Y + descLabelPadding + extrasSpacing;

			var leftWidth = new[] { nameSize.X, descSize.X, strengthsSize.X, weaknessesSize.X, attributesSize.X }.Aggregate(Math.Max);
			var rightWidth = new[] { armorTypeSize.X }.Aggregate(Math.Max);

			armorTypeIcon.Bounds.X = leftWidth + 2 * nameLabel.Bounds.X;
			armorTypeLabel.Bounds.X = armorTypeIcon.Bounds.Right + iconMargin;
			widget.Bounds.Width = leftWidth + rightWidth + 3 * nameLabel.Bounds.X + armorTypeIcon.Bounds.Width + iconMargin;

			// Set the bottom margin to match the left margin
			var leftHeight = descLabel.Bounds.Bottom + descLabel.Bounds.X;

			// Set the bottom margin to match the top margin
			var rightHeight = armorTypeIcon.Bounds.Bottom;
			widget.Bounds.Height = Math.Max(leftHeight, rightHeight);
			widget.Bounds.X = Game.Renderer.Resolution.Width - widget.Bounds.Width - 12;
			widget.Bounds.Y = Game.Renderer.Resolution.Height - widget.Bounds.Height - 12;
		}

		static string ActorName(Ruleset rules, string a)
		{
			if (rules.Actors.TryGetValue(a.ToLowerInvariant(), out var ai))
			{
				var actorTooltip = ai.TraitInfos<TooltipInfo>().FirstOrDefault(info => info.EnabledByDefault);
				if (actorTooltip != null)
					return actorTooltip.Name;
			}

			return a;
		}

		LabelWidget GetArmorTypeLabel(LabelWidget armorTypeLabel, ActorInfo actor)
		{
			var armor = actor.TraitInfos<ArmorInfo>().FirstOrDefault();
			armorTypeLabel.Text = armor != null ? armor.Type : "";

			if (armorTypeLabel.Text != "" && actor.HasTraitInfo<AircraftInfo>())
				armorTypeLabel.Text = "Aircraft";

			// hard coded, specific to CA - find a better way to set user-friendly names and colors for armor types
			switch (armorTypeLabel.Text)
			{
				case "None":
					armorTypeLabel.Text = "Infantry";
					armorTypeLabel.TextColor = Color.ForestGreen;
					break;

				case "Light":
					armorTypeLabel.TextColor = Color.MediumPurple;
					break;

				case "Heavy":
					armorTypeLabel.TextColor = Color.Firebrick;
					break;

				case "Concrete":
					armorTypeLabel.Text = "Defense";
					armorTypeLabel.TextColor = Color.RoyalBlue;
					break;

				case "Wood":
					armorTypeLabel.Text = "Building";
					armorTypeLabel.TextColor = Color.Peru;
					break;

				case "Brick":
					armorTypeLabel.Text = "Wall";
					armorTypeLabel.TextColor = Color.RosyBrown;
					break;

				case "Aircraft":
					armorTypeLabel.TextColor = Color.SkyBlue;
					break;

				default:
					armorTypeLabel.Text = "";
					break;
			}

			return armorTypeLabel;
		}
	}
}
