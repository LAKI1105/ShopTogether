// Used only by a disposable copy of the game during background tests.
// It keeps the subscribed Workshop item untouched while leaving the game's trust scan enabled.
using System;
using System.Collections;
using System.Reflection;

public static class IsolatedWorkshopRedirect {
    public static string PersistentPath() {
        string folder = Environment.GetEnvironmentVariable("SHOPTOGETHER_TEST_PERSISTENT_PATH");
        if (string.IsNullOrEmpty(folder) || !System.IO.Path.IsPathRooted(folder) || !System.IO.Directory.Exists(folder))
            throw new InvalidOperationException("The isolated test profile is missing");
        return folder;
    }
    public static object Apply(object items) {
        string folder = Environment.GetEnvironmentVariable("SHOPTOGETHER_PROBE_FOLDER");
        var list = items as IList;
        if (string.IsNullOrEmpty(folder) || list == null) return items;
        for (int i = 0; i < list.Count; i++) {
            object entry = list[i];
            if (entry == null) continue;
            FieldInfo id = entry.GetType().GetField("Item1"), path = entry.GetType().GetField("Item2");
            if (id == null || path == null || !(id.GetValue(entry) is ulong) || (ulong)id.GetValue(entry) != 3837477869UL) continue;
            path.SetValue(entry, folder);
            list[i] = entry;
        }
        return items;
    }
}
