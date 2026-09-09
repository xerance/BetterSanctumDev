using System;
using System.Collections.Generic;
using System.IO;

namespace BetterSanctumDev;

// Appends one row per distinct reward observation so a session's worth of Sanctum runs
// builds up a table to eyeball. Deduplicated in memory, so leaving it on and reopening
// the map repeatedly does not multiply rows.
public class RewardTracker
{
    private const string Header = "when;source;floor;floorPrefix;layer;room;slot;currency;detail";

    private readonly HashSet<string> _seen = new HashSet<string>();
    private readonly string _path;
    private bool _headerChecked;

    public RewardTracker(string path)
    {
        _path = path;
    }

    public string Path => _path;

    public int RowCount => _seen.Count;

    // Everything but the timestamp forms the key, so the same offer seen twice is one row
    // while a different quantity of the same currency is a new observation.
    public void Add(string source, int floor, string floorPrefix, string layer, string room, string slot, string currency, string detail)
    {
        var key = string.Join(";", source, floor, floorPrefix ?? "", layer, room, slot, currency, detail);
        if (!_seen.Add(key))
        {
            return;
        }

        try
        {
            if (!_headerChecked)
            {
                _headerChecked = true;
                if (!File.Exists(_path))
                {
                    File.AppendAllText(_path, Header + Environment.NewLine);
                }
            }

            File.AppendAllText(_path, $"{DateTime.Now:s};{key}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // A locked or unwritable file should not take the overlay down; the row is
            // already in _seen, so it simply will not be retried this session.
        }
    }
}
