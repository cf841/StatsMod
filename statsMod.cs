using Godot;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using System.Collections.Generic;

namespace StatsMod;

[ModInitializer("ModLoaded")]
public static class StatsMod
{
    private static readonly Dictionary<string, int> _damageDealtPerPlayer = new();

    public static void ModLoaded()
    {
        CombatManager.Instance.CombatSetUp += OnCombatSetUp;
        CombatManager.Instance.TurnEnded += OnTurnEnded;
    }

    private static void OnCombatSetUp(CombatState state)
    {
        _damageDealtPerPlayer.Clear();

        foreach (var enemy in state.Enemies)
        {
            enemy.CurrentHpChanged += (oldHp, newHp) =>
            {
                int damage = oldHp - newHp;
                if (damage > 0)
                {
                    // For now, attribute all damage to "Player" 
                    // until we find how damage source is tracked
                    string key = "Player";
                    _damageDealtPerPlayer.TryAdd(key, 0);
                    _damageDealtPerPlayer[key] += damage;
                }
            };
        }
    }

    private static void OnTurnEnded(CombatState state)
    {
        Log.Warn("=== Damage This Turn ===");
        foreach (var entry in _damageDealtPerPlayer)
        {
            Log.Warn($"{entry.Key}: {entry.Value} damage dealt");
        }
        _damageDealtPerPlayer.Clear(); // Reset each turn
    }
}