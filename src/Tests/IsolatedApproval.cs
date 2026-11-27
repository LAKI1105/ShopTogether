// User-authorized test automation. This helper is loaded only into a disposable game copy;
// it uses the game's normal scanner and approval API and never changes the real profile.
using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

public static class IsolatedApproval {
    static bool done;
    static bool Under(string child, string parent) {
        var root = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(child).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
    public static void Tick() {
        string mode=Environment.GetEnvironmentVariable("SHOPTOGETHER_TEST_ONLY_APPROVE");
        if (done || (mode != "1" && mode != "scan")) return;
        LoadedMod own=null;
        foreach (var mod in ModLoader.Mods) if(mod!=null&&mod.Manifest!=null) {
            if(mod.Manifest.ID=="shop_together")own=mod;
        }
        if(own==null)return;
        done = true;
        try {
            var root = Environment.GetEnvironmentVariable("SHOPTOGETHER_TEST_ROOT");
            var profile = Environment.GetEnvironmentVariable("SHOPTOGETHER_TEST_PERSISTENT_PATH");
            if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(profile) || !Under(Application.dataPath, root) || !Under(profile, root) || !Under(own.FolderPath, root))
                throw new InvalidOperationException("Approval must stay within the isolated game and test profile");
            if(own.Source!=ModSource.Workshop||own.WorkshopId!=3837477869UL||string.IsNullOrEmpty(own.Manifest.ModVersion))throw new InvalidOperationException("The test candidate identity changed");
            if(mode=="scan"&&own.Manifest.Capabilities.Contains("reflection-external"))throw new InvalidOperationException("Production must not declare deprecated reflection");
            if(mode=="scan"&&ModLoader.IsModEnabled(own.Manifest.ID))throw new InvalidOperationException("Production candidate must remain disabled during the isolated scan");
            Approve(own,new HashSet<string>(new[]{"network","steam","reflection-external"},StringComparer.Ordinal),mode!="scan");
            if(mode!="scan"&&Environment.GetEnvironmentVariable("SHOPTOGETHER_TEST_SMART_ARRANGE")=="1") {
                LoadedMod extra=null;foreach(var mod in ModLoader.Mods)if(mod.Manifest.ID=="smart_inventory_arrange")extra=mod;
                if(extra==null||!Under(extra.FolderPath,root))throw new InvalidOperationException("Missing isolated Smart Arrange");
                Approve(extra,new HashSet<string>(StringComparer.Ordinal),true);
            }
            if(mode!="scan"&&Environment.GetEnvironmentVariable("SHOPTOGETHER_TEST_STORAGE_PAGES")=="1") {
                LoadedMod extra=null;foreach(var mod in ModLoader.Mods)if(mod.Manifest.ID=="player.shop_storage_pages")extra=mod;
                if(extra==null||!Under(extra.FolderPath,root))throw new InvalidOperationException("Missing isolated storage pages");
                Approve(extra,new HashSet<string>(StringComparer.Ordinal),true);
            }
            Debug.Log("[SMC-TEST APPROVED] game scanner passed; requested capabilities authorized in isolated profile");
        } catch (Exception error) {
            Debug.LogError("[SMC-TEST APPROVAL-FAILED] " + error.ToString().Replace("\r", " ").Replace("\n", " | "));
        } finally { Application.Quit(); }
    }
    static void Approve(LoadedMod target,HashSet<string> allowed,bool enable){
        foreach(var capability in target.Manifest.Capabilities)if(!allowed.Contains(capability))throw new InvalidOperationException("The mod requests an additional capability: "+capability);
        var check=ModLoader.CheckEnable(target);
        if(check==null||check.Refusal!=null||check.Undeclared.Count!=0)throw new InvalidOperationException("Game scanner refused this mod: "+(check==null?"no result":check.Refusal??string.Join(",",check.Undeclared.ToArray())));
        if(check.Scan!=null)foreach(var finding in check.Scan.Findings)if(finding.Kind=="threads")Debug.Log("[SMC-TEST THREAD-SCAN] "+finding);
        foreach(var capability in check.PromptCapabilities)if(!allowed.Contains(capability))throw new InvalidOperationException("The game requests an additional capability: "+capability);
        if(check.Scan!=null)foreach(var finding in check.Scan.Findings)if(finding.Kind=="reflection-external"){if(!enable)throw new InvalidOperationException("Production uses deprecated reflection: "+finding);Debug.Log("[SMC-TEST SCAN] "+finding);}
        if(enable){ModLoader.ApproveAndEnable(target,check);
            if(!ModLoader.IsModEnabled(target.Manifest.ID)||ModTrust.Get(target.Manifest.ID)==null)throw new InvalidOperationException("The game did not record approval");}
    }
}
