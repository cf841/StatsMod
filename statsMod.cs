using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Rooms;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace StatsMod;

public class PlayerStats
{
    public string SteamId { get; set; } = "";
    public string CharacterName { get; set; } = "";
    public int DamageDealt { get; set; } = 0;
}

public class FightStats
{
    public Dictionary<string, PlayerStats> DamagePerPlayer { get; set; } = new();
}

[ModInitializer("ModLoaded")]
public static class StatsMod
{
    private static readonly List<FightStats> _allFights = new();
    private static FightStats _currentFight = new();
    private static string _filePath = "";
    private static int _lastProcessedEntryIndex = 0;

    public static void ModLoaded()
    {
        RunManager.Instance.RunStarted += OnRunStarted;
        CombatManager.Instance.CombatSetUp += OnCombatSetUp;
        CombatManager.Instance.CombatEnded += OnCombatEnded;
        SaveManager.Instance.Saved += WriteStats;
    }

    private static void OnRunStarted(RunState state)
    {
        _allFights.Clear();
        _filePath = Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments),
            $"sts2_stats_{System.DateTimeOffset.UtcNow.ToUnixTimeSeconds()}.json"
        );
        Log.Warn($"StatsMod: New run started, writing to {_filePath}");
    }

    private static void OnCombatSetUp(CombatState state)
    {
        _currentFight = new FightStats();
        _lastProcessedEntryIndex = 0;
        CombatManager.Instance.History.Changed -= OnHistoryChanged;
        CombatManager.Instance.History.Changed += OnHistoryChanged;

        foreach (var enemy in state.Enemies)
        {
            var capturedEnemy = enemy;

            capturedEnemy.CurrentHpChanged += (oldHp, newHp) =>
            {
                if (newHp == 0)
                {
                    // Check doom
                    var doom = capturedEnemy.GetPower<DoomPower>();
                    if (doom?.Applier?.IsPlayer == true && doom.IsOwnerDoomed())
                    {
                        AttributeDamage(doom.Applier.Player, oldHp);
                        return;
                    }

                    // Only handle poison kill here if combat is ending (last enemy)
                    // Otherwise OnHistoryChanged will handle it
                    if (CombatManager.Instance.IsEnding)
                    {
                        var poison = capturedEnemy.GetPower<PoisonPower>();
                        if (poison?.Applier?.IsPlayer == true)
                        {
                            AttributeDamage(poison.Applier.Player, oldHp);
                        }
                    }
                }
            };
        }
    }

    private static void OnHistoryChanged()
    {
        var entries = CombatManager.Instance.History.Entries.ToList();
        for (int i = _lastProcessedEntryIndex; i < entries.Count; i++)
        {
            ProcessEntry(entries[i]);
        }
        _lastProcessedEntryIndex = entries.Count;
    }

    private static void ProcessEntry(CombatHistoryEntry entry)
    {
        if (entry is DamageReceivedEntry dmg && dmg.Receiver.IsEnemy)
        {
            Player? dealer = null;

            if (dmg.Dealer?.IsPlayer == true)
            {
                dealer = dmg.Dealer.Player;
                Log.Warn($"Direct damage: {dmg.Result.UnblockedDamage} from {dealer.Character.Id.Entry}");
            }
            else if (dmg.Dealer?.IsPet == true)
            {
                dealer = dmg.Dealer.PetOwner;
                Log.Warn($"Pet damage: {dmg.Result.UnblockedDamage} from {dealer?.Character.Id.Entry}");
            }
            else if (dmg.Dealer == null)
            {
                var poison = dmg.Receiver.GetPower<PoisonPower>();
                if (poison?.Applier?.IsPlayer == true)
                {
                    dealer = poison.Applier.Player;
                    Log.Warn($"Poison damage: {dmg.Result.UnblockedDamage} from {dealer.Character.Id.Entry}");
                }
            }

            if (dealer != null)
                AttributeDamage(dealer, dmg.Result.UnblockedDamage);
        }
    }

    private static void OnCombatEnded(CombatRoom room)
    {
        // Flush any final entries before closing out
        OnHistoryChanged();
        _allFights.Add(_currentFight);
        CombatManager.Instance.History.Changed -= OnHistoryChanged;
        WriteStats();
    }

    private static void AttributeDamage(Player player, int damage)
    {
        string steamId = player.NetId.ToString();
        if (!_currentFight.DamagePerPlayer.ContainsKey(steamId))
        {
            _currentFight.DamagePerPlayer[steamId] = new PlayerStats
            {
                SteamId = steamId,
                CharacterName = player.Character.Id.Entry
            };
        }
        _currentFight.DamagePerPlayer[steamId].DamageDealt += damage;
    }

    private static void WriteStats()
    {
        if (_allFights.Count == 0 || string.IsNullOrEmpty(_filePath)) return;
        string json = JsonSerializer.Serialize(_allFights, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
        Log.Warn($"StatsMod: Stats written to {_filePath}");
    }
}