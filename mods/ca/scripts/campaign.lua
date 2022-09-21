--[[
   Copyright 2007-2022 The OpenRA Developers (see AUTHORS)
   This file is part of OpenRA, which is free software. It is made
   available to you under the terms of the GNU General Public License
   as published by the Free Software Foundation, either version 3 of
   the License, or (at your option) any later version. For more
   information, see COPYING.
]]

Difficulty = Map.LobbyOption("difficulty")

InitObjectives = function(player)
	Trigger.OnObjectiveAdded(player, function(p, id)
		Media.DisplayMessage(p.GetObjectiveDescription(id), "New " .. string.lower(p.GetObjectiveType(id)) .. " objective")
	end)

	Trigger.OnObjectiveCompleted(player, function(p, id)
		Media.DisplayMessage(p.GetObjectiveDescription(id), "Objective completed")
		Media.PlaySoundNotification(Greece, "AlertBleep")
	end)

	Trigger.OnObjectiveFailed(player, function(p, id)
		Media.DisplayMessage(p.GetObjectiveDescription(id), "Objective failed")
	end)

	Trigger.OnPlayerLost(player, function()
		Trigger.AfterDelay(DateTime.Seconds(1), function()
			Media.PlaySpeechNotification(player, "MissionFailed")
		end)
	end)

	Trigger.OnPlayerWon(player, function()
		Trigger.AfterDelay(DateTime.Seconds(1), function()
			Media.PlaySpeechNotification(player, "MissionAccomplished")
		end)
	end)
end

AttackAircraftTargets = { }
InitializeAttackAircraft = function(aircraft, enemyPlayer)
	Trigger.OnIdle(aircraft, function()
		local actorId = tostring(aircraft)
		local target = AttackAircraftTargets[actorId]

		if not target or not target.IsInWorld then
			target = ChooseRandomTarget(aircraft, enemyPlayer)
		end

		if target then
			AttackAircraftTargets[actorId] = target
			aircraft.Attack(target)
		else
			AttackAircraftTargets[actorId] = nil
			aircraft.ReturnToBase()
		end
	end)
end

ChooseRandomTarget = function(unit, enemyPlayer)
	local target = nil
	local enemies = Utils.Where(enemyPlayer.GetActors(), function(self)
		return self.HasProperty("Health") and unit.CanTarget(self) and not Utils.Any({ "sbag", "fenc", "brik", "cycl", "barb" }, function(type) return self.Type == type end)
	end)
	if #enemies > 0 then
		target = Utils.Random(enemies)
	end
	return target
end

ChooseRandomBuildingTarget = function(unit, enemyPlayer)
	local target = nil
	local enemies = Utils.Where(enemyPlayer.GetActors(), function(self)
		return self.HasProperty("Health") and self.HasProperty("StartBuildingRepairs") and unit.CanTarget(self) and not Utils.Any({ "sbag", "fenc", "brik", "cycl", "barb" }, function(type) return self.Type == type end)
	end)
	if #enemies > 0 then
		target = Utils.Random(enemies)
	end
	return target
end

OnAnyDamaged = function(actors, func)
	Utils.Do(actors, function(actor)
		Trigger.OnDamaged(actor, func)
	end)
end

IdleHunt = function(actor)
	if actor.HasProperty("Hunt") and not actor.IsDead then
		Trigger.OnIdle(actor, actor.Hunt)
	end
end

ClearTriggersStopAndHunt = function(a)
	if not a.IsDead then
		Trigger.ClearAll(a)
		a.Stop()
		if a.HasProperty("Hunt") then
			a.Hunt()
		end
	end
end

AutoRepairBuildings = function(player)
	local buildings = Utils.Where(Map.ActorsInWorld, function(self) return self.Owner == player and self.HasProperty("StartBuildingRepairs") end)
	Utils.Do(buildings, function(actor)
		Trigger.OnDamaged(actor, function(building)
			if building.Owner == player and building.Health < (building.MaxHealth * 75 / 100) then
				building.StartBuildingRepairs()
			end
		end)
	end)
end

-- Returns true if player has one of any of the specified actor types
HasOneOf = function(player, types)
	local count = 0

	Utils.Do(types, function(name)
		if #player.GetActorsByType(name) > 0 then
			count = count + 1
		end
	end)

	return count > 0
end

-- Make specified units have a chance to swap targets when attacked instead of chasing one target endlessly
TargetSwapChance = function(units, player, chance)
	Utils.Do(units, function(unit)
		Trigger.OnDamaged(unit, function(self, attacker, damage)
			if attacker.EffectiveOwner == player then
				return
			end
			local rand = Utils.RandomInteger(1,100)
			if rand > 100 - chance then
				if unit.HasProperty("Attack") and not unit.IsDead then
					unit.Stop()
					unit.Attack(attacker)
				end
			end
		end)
	end)
end
