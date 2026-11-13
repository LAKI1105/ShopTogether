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
        if (done || Environment.GetEnvironmentVariable("SHOPTOGETHER_TEST_ONLY_APPROVE") != "1") return;
        bool withVisitor=Environment.GetEnvironmentVariable("SHOPTOGETHER_TEST_WITH_VISITOR")=="1";
        LoadedMod own=null, visitor=null;
        foreach (var mod in ModLoader.Mods) if(mod!=null&&mod.Manifest!=null) {
            if(mod.Manifest.ID=="shop_together")own=mod;
            if(mod.Manifest.ID=="lemocuber_probablymultiplayer")visitor=mod;
        }
        if(own==null||(withVisitor&&visitor==null))return;
        done = true;
        try {
            var root = Environment.GetEnvironmentVariable("SHOPTOGETHER_TEST_ROOT");
            var profile = Environment.GetEnvironmentVariable("SHOPTOGETHER_TEST_PERSISTENT_PATH");
            if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(profile) || !Under(Application.dataPath, root) || !Under(profile, root) || !Under(own.FolderPath, root) || (withVisitor&&!Under(visitor.FolderPath,root)))
                throw new InvalidOperationException("Approval must stay within the isolated game and test profile");
            if(own.Source!=ModSource.Workshop||own.WorkshopId!=3837477869UL||string.IsNullOrEmpty(own.Manifest.ModVersion))throw new InvalidOperationException("The test candidate identity changed");
            Approve(own,new HashSet<string>(new[]{"network","steam","reflection-external"},StringComparer.Ordinal));
            if(withVisitor){
                if(visitor.Source!=ModSource.Workshop||visitor.WorkshopId!=3828909239UL||visitor.Manifest.ModVersion!="2.6.1")throw new InvalidOperationException("The requested visitor mod or version changed");
                Approve(visitor,new HashSet<string>(new[]{"network","process","steam-auth"},StringComparer.Ordinal));
            }
            Debug.Log("[PM-TEST APPROVED] game scanner passed; requested capabilities authorized in isolated profile");
        } catch (Exception error) {
            Debug.LogError("[PM-TEST APPROVAL-FAILED] " + error);
        } finally { Application.Quit(); }
    }
    static void Approve(LoadedMod target,HashSet<string> allowed){
        foreach(var capability in target.Manifest.Capabilities)if(!allowed.Contains(capability))throw new InvalidOperationException("The mod requests an additional capability: "+capability);
        var check=ModLoader.CheckEnable(target);
        if(check==null||check.Refusal!=null||check.Undeclared.Count!=0)throw new InvalidOperationException("Game scanner refused this mod: "+(check==null?"no result":check.Refusal??string.Join(",",check.Undeclared.ToArray())));
        foreach(var capability in check.PromptCapabilities)if(!allowed.Contains(capability))throw new InvalidOperationException("The game requests an additional capability: "+capability);
        if(check.Scan!=null)foreach(var finding in check.Scan.Findings)if(finding.Kind=="reflection-external")Debug.Log("[PM-TEST SCAN] "+finding);
        ModLoader.ApproveAndEnable(target,check);
        if(!ModLoader.IsModEnabled(target.Manifest.ID)||ModTrust.Get(target.Manifest.ID)==null)throw new InvalidOperationException("The game did not record approval");
    }
}
