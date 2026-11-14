#if LAN_PROBE
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using UnityEngine;
namespace ShopTogether {
    internal static partial class RuntimeProbe {
        static readonly string[] authorityMachines = { "furnace", "hydroponic", "plant_dna_manipulator", "printer", "recharger", "security_alarm", "mirage_projector", "moisture_farm", "water_purifier", "desequencer", "evaporator", "wine_rack", "animal_cage", "vacuum_robot", "mouse_trap", "ras", "turbo_booster_adv", "turbo_booster_hypertech", "wine_bottle", "vending_machine", "vending_fountain" };
        static string PanelTexts134(GameItem item) {
            var texts = new List<string>();
            foreach (var node in item.contentWindow.handler.GetComponentsInChildren<TreeNodeRender>(true)) {
                var text = node.element as MachineryText; if (text != null) texts.Add(text.GetText());
            }
            return string.Join("|", texts);
        }
        static IEnumerator AuthorityHost134(Connection root) {
            var e = EmporiumEntry.Instance; e.invElement.SetShape(70,70); e.Validate(true);
            for (int i=0;i<authorityMachines.Length;i++) PanelFixture(authorityMachines[i], "AUTH_MACHINE_"+i);
            PanelFixture("chem_finisher", "AUTH_EMPTY");
            var moving=PanelFixture("screwdriver", "AUTH_MOVE");
            var furnace=Find(root,"AUTH_MACHINE_0"); // indexed below; native fixture can also be found through the inventory
            root.World.Index(); furnace=Find(root,"AUTH_MACHINE_0");
            NightPower(furnace);
            var scrap=DirectoryMaster.Item("scrap_metal");GeneralHelper.SetItemOwned(scrap,true);scrap.unitCount=2;
            Require(GraphUtils.TryAcceptAll(MachineHelper.GetBodyStorage(furnace,2),scrap)==0,"authority furnace has native overnight input");
            root.World.Index(); Signal("AUTH_READY");
            var wait=Wait(()=>Received("AUTH_OPEN"),"guest opened all 21 machine windows",120);while(wait.MoveNext())yield return wait.Current;
            PlayerStore.Instance.EndNight();
            Require(ChildCount(furnace,"metal_ingot")>0,"host furnace produced an ingot during the actual night cycle");
            Find(root,"AUTH_MACHINE_19").ModifyTag("VENDING_CASH",t=>t.Enable().SetInt(321));
            Find(root,"AUTH_MACHINE_20").ModifyTag("VENDING_CASH",t=>t.Enable().SetInt(654));
            root.World.Index();
            // Native day callbacks update these labels on the host; reopen to take the game's
            // authoritative labels, while the guest deliberately leaves its windows open.
            var hashes=new List<string>();var textHashes=new List<string>();
            for(int i=0;i<authorityMachines.Length;i++) {
                var item=Find(root,"AUTH_MACHINE_"+i);item.contentWindow.Show();
                hashes.Add(Fingerprint.Item(root.World,root.World.Id(item),item).ToString());
                textHashes.Add(Fingerprint.Hash(PanelTexts134(item)).ToString());
            }
            Signal("AUTH_ITEMS="+string.Join(",",hashes)); Signal("AUTH_TEXT="+string.Join(",",textHashes));
            wait=Wait(()=>Received("AUTH_DONE"),"guest completed authoritative operations and 21 machine displays",150);while(wait.MoveNext())yield return wait.Current;
            Require(moving.parentInventory==e.invElement,"rejected guest placement preserved the source item");
            Require(!Find(root,"AUTH_EMPTY").IsTag("SELECTED_CHEM_ID_TAG"),"host rejected a nonexistent recipe");
        }
        static IEnumerator AuthorityClient134(Connection root) {
            var wait=Wait(()=>Find(root,"AUTH_MACHINE_20")!=null&&Find(root,"AUTH_EMPTY")!=null&&Find(root,"AUTH_MOVE")!=null&&Received("AUTH_READY"),"authority fixtures replicated",120);while(wait.MoveNext())yield return wait.Current;
            for(int i=0;i<authorityMachines.Length;i++)Find(root,"AUTH_MACHINE_"+i).contentWindow.Show();
            Signal("AUTH_OPEN"); string items=null,texts=null;
            wait=Wait(()=>(items=items??ReceivePrefix("AUTH_ITEMS="))!=null&&(texts=texts??ReceivePrefix("AUTH_TEXT="))!=null,"host night results and labels received",120);while(wait.MoveNext())yield return wait.Current;
            var ih=items.Split(',');var th=texts.Split(',');
            wait=Wait(()=>{
                for(int i=0;i<authorityMachines.Length;i++) { var item=Find(root,"AUTH_MACHINE_"+i); if(item==null||Fingerprint.Item(root.World,root.World.Id(item),item).ToString()!=ih[i]||Fingerprint.Hash(PanelTexts134(item)).ToString()!=th[i])return false; }
                return true;
            },"all 21 machine states and visible text match after the night without reopening guest windows",45);while(wait.MoveNext())yield return wait.Current;
            var moving=Find(root,"AUTH_MOVE"); var ui=ItemContextHandler.current;
            AccessTools.Field(typeof(ItemContextHandler),"currentItem").SetValue(ui,moving);
            AccessTools.Field(typeof(ItemContextHandler),"canActivate").SetValue(ui,false);
            var button=AccessTools.Field(typeof(ItemContextHandler),"buttonActivate").GetValue(ui);
            RemoteActions.LastNote=null;
            NativeClick(0,()=>AccessTools.Method(typeof(ItemContextHandler),"TriggerButton").Invoke(ui,new[]{button}));
            wait=Wait(()=>RemoteActions.LastNote==Loc.Text("~SMC317~"),"grey context action reached host and returned its refusal",30);while(wait.MoveNext())yield return wait.Current;
            root.ReleaseAll(); RemoteActions.LastNote=null;
            long version;root.World.Versions.TryGetValue(root.World.Id(moving),out version);
            root.Submit(new Request{kind=RequestKind.Move,items=new[]{root.World.Id(moving)},moves=new[]{new Placement{item=root.World.Id(moving),count=1,version=version,inventory=root.World.Address(EmporiumEntry.Instance.invElement),x=999,y=999}}});
            wait=Wait(()=>RemoteActions.LastNote==Loc.Text("~SMC314~"),"invalid placement returned destination reason",30);while(wait.MoveNext())yield return wait.Current;
            root.ReleaseAll(); RemoteActions.LastNote=null;
            var machine=Find(root,"AUTH_EMPTY");MachineClick(machine,1);
            wait=Wait(()=>RemoteActions.LastNote==Loc.Text("~SMC321~"),"empty machine button returned no-change explanation",30);while(wait.MoveNext())yield return wait.Current;
            RemoteActions.LastNote=null;
            SettingsClick(()=>machine.ModifyTag("SELECTED_CHEM_ID_TAG",t=>t.Enable().SetString("missing_recipe_134")));
            Require(!machine.IsTag("SELECTED_CHEM_ID_TAG"),"guest setting does not mutate shared state before host verdict");
            wait=Wait(()=>RemoteActions.LastNote==Loc.Text("~SMC327~"),"host rejected nonexistent recipe with reason",30);while(wait.MoveNext())yield return wait.Current;
            root.ReleaseAll();Signal("AUTH_DONE");
        }
        static IEnumerator AuthorityRecipeHost134(Connection root) {
            var machine=PanelFixture("chem_finisher","AUTH_RECIPE");root.World.Index();Signal("RECIPE134_READY");
            var wait=Wait(()=>Received("RECIPE134_DONE"),"corrected settings input received host refusal",60);while(wait.MoveNext())yield return wait.Current;
            Require(!machine.IsTag("SELECTED_CHEM_ID_TAG"),"invalid recipe did not change the host machine");
        }
        static IEnumerator AuthorityRecipeClient134(Connection root) {
            var wait=Wait(()=>Find(root,"AUTH_RECIPE")!=null&&Received("RECIPE134_READY"),"recipe fixture replicated",60);while(wait.MoveNext())yield return wait.Current;
            var machine=Find(root,"AUTH_RECIPE");RemoteActions.LastNote=null;
            SettingsClick(()=>{ChemFinisherUIManager.Instance.currentItem=machine;ChemFinisherUIManager.Instance.OnChemRecipeSelected("missing_recipe_134");});
            Require(!machine.IsTag("SELECTED_CHEM_ID_TAG"),"recipe selection waits for the host before mutating state");
            wait=Wait(()=>RemoteActions.LastNote==Loc.Text("~SMC327~"),"invalid recipe refusal returned through the actual settings input scope",30);while(wait.MoveNext())yield return wait.Current;
            Signal("RECIPE134_DONE");
        }
    }
}
#endif
