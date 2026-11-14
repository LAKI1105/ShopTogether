#if LAN_PROBE
using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace ShopTogether {
    internal static partial class RuntimeProbe {
        static List<string> AuditVariableKeys() {
            var keys = new List<string>();
            foreach (var liquid in Liquid.Liquids) { keys.Add(liquid.currentPart); keys.Add(liquid.part); }
            foreach (var trait in PlantTraitRegistry.All) keys.Add("PLANT_TRAIT_" + trait.id);
            foreach (string gene in AnimalSpecies.Rat.geneticTraitIDs)
                foreach (string suffix in new[] { "", "_ALLELE_A", "_ALLELE_B", "_PARTNER_ALLELE_A", "_PARTNER_ALLELE_B", "_BONUS", "_BONUS_PERCENT" }) keys.Add(gene + suffix);
            keys.AddRange(new[] { "CONTAINER_TEMP_TAG", "CHEMICAL_PRODUCT_PURITY_TAG", "GENETIC_HEAT_INT", "PLANT_HEALTH_INT", "GROWING_PROGRESS", "DNA_DISK_TRAIT_STRING", "DNA_DISK_RANK_INT", "DNA_DISK_USES_INT" });
            return keys;
        }
        static void AuditVariables(GameItem item, int phase) {
            int n = 0;
            foreach (string key in AuditVariableKeys()) {
                int k = ++n;
                item.ModifyTag(key, t => { t.valueEnabled = phase == 1 || k % 3 != 0; t.valueInt = phase == 1 ? k * 1003 : 0; t.valueFloat = k + phase * .125f; t.valueDouble = k + phase * .000125; t.valueLong = 9000000000L + k + phase; t.valueBool = phase == 1; t.valueString = "state-" + k + "-" + phase; });
            }
        }
        static object AuditCompanyState(string id) {
            var type=AccessTools.TypeByName("CorpPresence.Companies");
            var company=AccessTools.Method(type,"Find").Invoke(null,new object[]{id});
            return AccessTools.Method(type,"StateOf").Invoke(null,new[]{company});
        }
        static bool AuditDealer() { var state=AuditCompanyState("pastel");return (bool)AccessTools.Property(state.GetType(),"DealerJoined").GetValue(state,null); }
        static IEnumerator AuditHost135(Connection root) {
            var e = EmporiumEntry.Instance; e.invElement.SetShape(160,256); e.Validate(true);
            Require(!MachineSettings.Allowed("LIQUID_CONTAINER_CAPACITY") && !MachineSettings.Allowed("CURRENT_PART_ACID") && !MachineSettings.Allowed("PLANT_TRAIT_edible") && !MachineSettings.Allowed("ANIMAL_FERTILITY_TAG_ALLELE_A") && MachineSettings.Allowed("HOPPER_KEEP_INT"),"raw settings whitelist rejects simulation results");
            Time.timeScale = 0f; // Stop the native clock only in this disposable fixture, not the network clock.
            IEnumerator wait;GameItem data;
            if(Argument("--smc-suite")=="audit135"){
            var ids = DirectoryMaster.GetIdentifierList<GameItem>(); ids.Sort(StringComparer.Ordinal);
            Require(ids.Count >= 516, "loaded catalog registration count: " + ids.Count);
            Debug.Log("[SMC AUDIT CATALOG] " + string.Join(",",ids));
            foreach(var component in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
                if(component is IClickable && component.gameObject.scene.IsValid()) Debug.Log("[SMC AUDIT OBJECT] "+component.GetType().FullName+" | "+component.gameObject.name);
            var fixtures = new List<GameItem>(); int x = 0, y = 0, row = 0;
            foreach (string id in ids) {
                // Catalog serialization also covers system items that deliberately reject a
                // normal player placement. Install fixture roots without using that gameplay action.
                var item = ModHelper.SpawnItem(id); if(item==null)throw new InvalidOperationException("catalog factory: "+id);
                item.name = "AUDIT135_" + fixtures.Count; GeneralHelper.SetItemOwned(item,true);
                int width = Math.Max(1,item.modifiedShape.width), height = Math.Max(1,item.modifiedShape.height);
                if (x + width > 160) { x = 0; y += row; row = 0; }
                item.modifiedShape = new GridShapeBuilder(item.modifiedShape).SetPosition(x,y).Build();
                if (item.parentInventory != null) item.parentInventory.Expel(item);
                if(!e.invElement.UncheckedAccept(item))throw new InvalidOperationException("catalog fixture registration: "+id);
                x += width; row = Math.Max(row,height); fixtures.Add(item);
                if (fixtures.Count % 16 == 0) yield return null;
            }
            foreach (var item in fixtures) item.Validate();
            root.World.Index();
            var hashes = new List<string>(); foreach (var item in fixtures) hashes.Add(root.World.Id(item) + ":" + Fingerprint.Item(root.World, root.World.Id(item), item).ToString());
            foreach(var item in fixtures)if(item.identifier=="pink_alt_injector")Debug.Log("[SMC AUDIT PINK HOST] "+Fingerprint.Describe(root.World,root.World.Id(item),item));
            Signal("AUDIT135_COUNT=" + hashes.Count);
            for(int start=0;start<hashes.Count;start+=32) {
                int chunk=start/32;Signal("AUDIT135_CHUNK_"+chunk+"="+string.Join(",",hashes.GetRange(start,Math.Min(32,hashes.Count-start))));
                wait=Wait(()=>Received("AUDIT135_ACK_"+chunk),"bounded catalog digest chunk "+chunk,60);while(wait.MoveNext())yield return wait.Current;
            }
            wait = Wait(() => Received("AUDIT135_CATALOG_OK"), "every registered item reproduced with matching authoritative fields", 180); while (wait.MoveNext()) yield return wait.Current;
            // Exercise every TagState channel, including disabling/zeroing fields, over the actual connection.
            data = PanelFixture("screwdriver", "AUDIT135_VARIABLES"); AuditVariables(data, 1); root.World.Index();
            Signal("AUDIT135_V1=" + Fingerprint.Item(root.World, root.World.Id(data), data));
            wait = Wait(() => Received("AUDIT135_V1_OK"), "all liquid, plant trait and animal allele state channels replicated", 90); while (wait.MoveNext()) yield return wait.Current;
            long oldHash = Fingerprint.Item(root.World, root.World.Id(data), data); AuditVariables(data, 2);
            Require(oldHash != Fingerprint.Item(root.World, root.World.Id(data), data), "drift fingerprint includes TagSystem dictionary values");
            Signal("AUDIT135_V2=" + Fingerprint.Item(root.World, root.World.Id(data), data));
            wait = Wait(() => Received("AUDIT135_V2_OK"), "zero values and disabled tags replaced previous guest values", 90); while (wait.MoveNext()) yield return wait.Current;
            }else{data=PanelFixture("screwdriver","AUDIT135_VARIABLES");}
            var plate = PanelFixture("heating_plate", "AUDIT135_PLATE"); root.World.Index(); Signal("AUDIT135_CONTROL");
            wait = Wait(() => plate.GetTagReadonly("HEATING_PLATE_STATE").GetInt() == 1, "guest radio intent executed on host", 45); while (wait.MoveNext()) yield return wait.Current;
            wait = Wait(() => Received("AUDIT135_DONE"), "guest observed shared control result and host refusal", 45); while (wait.MoveNext()) yield return wait.Current;
            var water = PanelFixture("small_bottled_water", "AUDIT135_WATER"); WaterHelper.FillWithPureWater(water);
            water.ModifyTag(Liquid.GetCurrentPartFromId("acid"),t=>t.Enable().SetInt(10000));
            water.ModifyTag(Liquid.GetCurrentPartFromId("base"),t=>t.Enable().SetInt(10000));
            Require(ChemicalHelper.HandleAcidBaseNeutralization(water)>0,"native host acid/base reaction changed contents");
            var target = PanelFixture("1ml_dropper", "AUDIT135_DROP"); WaterHelper.TransferLiquid(water,target);
            root.World.Index(); Signal("AUDIT135_WATER="+Fingerprint.Item(root.World,root.World.Id(water),water)+","+Fingerprint.Item(root.World,root.World.Id(target),target));
            wait = Wait(() => water.IsTag("LAST_SCAN_RESULT_TAG") && data.GetTagReadonly("APPRAISAL_DEFAULT_VALUE").GetInt()==4321, "host recorded scanner result and new appraisal input", 60); while(wait.MoveNext())yield return wait.Current;
            wait = Wait(() => Received("AUDIT135_SCANNED"), "guest saw scanned variables and appraisal result", 60); while(wait.MoveNext())yield return wait.Current;
            var transfer = PanelFixture("screwdriver","AUDIT135_TRANSFER");e.invElement.Expel(transfer);e.frontInvinvElement.UncheckedAccept(transfer);e.frontInvWindow.Hide();root.World.Index();Signal("AUDIT135_TRANSFER_READY");
            wait=Wait(()=>transfer.parentInventory==e.backInvinvElement,"counter transfer used guest front-window intent while host window was closed",60);while(wait.MoveNext())yield return wait.Current;
            wait=Wait(()=>Received("AUDIT135_TRANSFER_DONE"),"counter transfer synchronized back to guest",60);while(wait.MoveNext())yield return wait.Current;
            var companyState=AuditCompanyState("pastel");AccessTools.Property(companyState.GetType(),"DealerJoined").SetValue(companyState,false,null);AccessTools.Property(companyState.GetType(),"Coop").SetValue(companyState,1000,null);
            PlayerStore.Instance.playerCash=100000;CompanyIntent.Flush(true);Signal("AUDIT135_COMPANY_READY");
            wait=Wait(AuditDealer,"company service intent executed on host",60);while(wait.MoveNext())yield return wait.Current;
            Require(PlayerStore.Instance.playerCash<100000,"company service charged host cash");
            wait=Wait(()=>Received("AUDIT135_COMPANY_DONE"),"company state and host rejection synchronized",60);while(wait.MoveNext())yield return wait.Current;
            var plant=PanelFixture("nutrifruit_seed","AUDIT135_PLANT");plant.EnableTag("GROWING_TAG");plant.ModifyTag("PLANT_HEALTH_INT",t=>t.Enable().SetInt(1234));
            PlantAnalyserUIManager.Instance.OpenUI(plant);PlantAnalyserUIManager.Instance.Refresh();root.World.Index();
            Signal("AUDIT135_PLANT1="+Fingerprint.Hash(PlantAnalyserUIManager.Instance.body2TMP.text));
            wait=Wait(()=>Received("AUDIT135_PLANT_OPEN"),"guest plant analyser opened",60);while(wait.MoveNext())yield return wait.Current;
            plant.ModifyTag("PLANT_HEALTH_INT",t=>t.Enable().SetInt(5678));PlantAnalyserUIManager.Instance.Refresh();
            Signal("AUDIT135_PLANT2="+Fingerprint.Hash(PlantAnalyserUIManager.Instance.body2TMP.text));
            wait=Wait(()=>Received("AUDIT135_PLANT_DONE"),"plant analyser refreshed changed host variable without reopening",60);while(wait.MoveNext())yield return wait.Current;
            Time.timeScale = 1f;
        }
        static IEnumerator AuditClient135(Connection root) {
            IEnumerator wait;
            if(Argument("--smc-suite")=="audit135"){
            string catalog = null;
            wait = Wait(() => (catalog = catalog ?? ReceivePrefix("AUDIT135_COUNT=")) != null, "host catalog count received", 180); while (wait.MoveNext()) yield return wait.Current;
            int count=int.Parse(catalog);var allHashes=new List<string>();
            for(int start=0;start<count;start+=32) {
                int chunk=start/32;string part=null;
                wait=Wait(()=>(part=part??ReceivePrefix("AUDIT135_CHUNK_"+chunk+"="))!=null,"bounded catalog chunk arrived "+chunk,60);while(wait.MoveNext())yield return wait.Current;
                allHashes.AddRange(part.Split(','));Signal("AUDIT135_ACK_"+chunk);
            }
            var records=allHashes.ToArray();var hashes=new string[records.Length];var itemIds=new int[records.Length];for(int i=0;i<records.Length;i++){var pair=records[i].Split(':');itemIds[i]=int.Parse(pair[0]);hashes[i]=pair[1];}var copies=new GameItem[hashes.Length];var matched=new bool[hashes.Length];int cursor=0,matching=0;string mismatch="";bool pinkLogged=false;
            wait = Wait(() => {
                for(int n=0;n<copies.Length;n++){GameItem item;if(root.World.Items.TryGetValue(itemIds[n],out item))copies[n]=item;}
                for(int check=0;check<16;check++) { int n=cursor++%hashes.Length;var item=copies[n];bool same=item!=null&&Fingerprint.Item(root.World,root.World.Id(item),item).ToString()==hashes[n];if(same!=matched[n]){matching+=same?1:-1;matched[n]=same;}if(!same)mismatch=item==null?"missing index "+n:item.identifier;if(!same&&!pinkLogged&&item!=null&&item.identifier=="pink_alt_injector"){pinkLogged=true;Debug.Log("[SMC AUDIT PINK CLIENT] "+Fingerprint.Describe(root.World,root.World.Id(item),item));} }
                return matching==hashes.Length;
            }, "all " + hashes.Length + " registered items match host fields and hierarchy", 180);
            while(true) { bool more;try { more=wait.MoveNext(); } catch(Exception error) { throw new InvalidOperationException("Catalog matched "+matching+"/"+hashes.Length+"; last mismatch "+mismatch,error); } if(!more)break;yield return wait.Current; }
            Signal("AUDIT135_CATALOG_OK");
            for (int phase = 1; phase <= 2; phase++) {
                string expected = null; string prefix = "AUDIT135_V" + phase + "=";
                wait = Wait(() => (expected = expected ?? ReceivePrefix(prefix)) != null && Find(root,"AUDIT135_VARIABLES") != null, "shared variable phase " + phase + " arrived", 90); while (wait.MoveNext()) yield return wait.Current;
                var item = Find(root,"AUDIT135_VARIABLES");
                wait = Wait(() => Fingerprint.Item(root.World, root.World.Id(item), item).ToString() == expected, "all " + AuditVariableKeys().Count + " variable records match in phase " + phase, 90); while (wait.MoveNext()) yield return wait.Current;
                Signal("AUDIT135_V" + phase + "_OK");
            }
            }
            bool controlReady=false;
            wait = Wait(() => (controlReady=controlReady||Received("AUDIT135_CONTROL")) && Find(root,"AUDIT135_PLATE") != null, "heating plate controls replicated", 60); while (wait.MoveNext()) yield return wait.Current;
            var plate = Find(root,"AUDIT135_PLATE"); plate.contentWindow.Show();
            var radio = ControlIntent.Controls(plate)[1] as MachineryRadioButton; Require(radio != null, "heating plate native radio found");
            radio.SetDisabled(true); // Deliberately stale disabled hint on this guest.
            NativeClick(0, () => AccessTools.Method(typeof(MachineryRadioButton), "OnSelect").Invoke(radio,null));
            Require(plate.GetTagReadonly("HEATING_PLATE_STATE").GetInt() != 1, "radio click did not run guest gameplay callback");
            wait = Wait(() => plate.GetTagReadonly("HEATING_PLATE_STATE").GetInt() == 1, "stale disabled radio reached host and synchronized result", 45); while (wait.MoveNext()) yield return wait.Current;
            RemoteActions.LastNote = null;
            RemoteActions.Forward(RemoteActions.Adapter("SetControl",5),null,new object[]{plate,999,"invalid",1f,0f});
            wait = Wait(() => RemoteActions.LastNote == Loc.Text("~SMC305~"), "invalid control request returned host reason", 30); while (wait.MoveNext()) yield return wait.Current;
            Signal("AUDIT135_DONE");
            string waterHashes=null;
            wait=Wait(()=>(waterHashes=waterHashes??ReceivePrefix("AUDIT135_WATER="))!=null&&Find(root,"AUDIT135_WATER")!=null&&Find(root,"AUDIT135_DROP")!=null,"host reacted and transferred liquid",60);while(wait.MoveNext())yield return wait.Current;
            var expectedWater=waterHashes.Split(',');var water=Find(root,"AUDIT135_WATER");var drop=Find(root,"AUDIT135_DROP");
            wait=Wait(()=>Fingerprint.Item(root.World,root.World.Id(water),water).ToString()==expectedWater[0]&&Fingerprint.Item(root.World,root.World.Id(drop),drop).ToString()==expectedWater[1],"native acid/base and proportional transfer results match on guest",60);while(wait.MoveNext())yield return wait.Current;
            var scanner=WaterScannerUIManager.Instance; scanner.currentWaterContainer=water;scanner.panel.SetActive(true);scanner.RefreshUI();
            var data=Find(root,"AUDIT135_VARIABLES");var inspection=InspectionUIManager.Instance;
            AccessTools.Field(typeof(InspectionUIManager),"currentInspectingItem").SetValue(inspection,data);inspection.apprisalServiceInputField.text="4321";
            NativeClick(0,()=>inspection.FinishEditingAppraisalField());
            Require(data.GetTagReadonly("APPRAISAL_DEFAULT_VALUE").GetInt()!=4321,"appraisal input waits for host");
            wait=Wait(()=>water.IsTag("LAST_SCAN_RESULT_TAG")&&data.GetTagReadonly("APPRAISAL_DEFAULT_VALUE").GetInt()==4321,"host scan history and appraisal replicated",60);while(wait.MoveNext())yield return wait.Current;
            Signal("AUDIT135_SCANNED");
            bool transferReady=false;
            wait=Wait(()=>(transferReady=transferReady||Received("AUDIT135_TRANSFER_READY"))&&Find(root,"AUDIT135_TRANSFER")!=null,"counter transfer fixture arrived",60);while(wait.MoveNext())yield return wait.Current;
            EmporiumEntry.Instance.frontInvWindow.Show();
            NativeClick(0,()=>UnityEngine.Object.FindObjectOfType<StoreTransferButton>().OnClick());
            wait=Wait(()=>Find(root,"AUDIT135_TRANSFER").parentInventory==EmporiumEntry.Instance.backInvinvElement,"guest front-to-back transfer result arrived",60);while(wait.MoveNext())yield return wait.Current;
            Signal("AUDIT135_TRANSFER_DONE");
            bool companyReady=false;
            wait=Wait(()=>(companyReady=companyReady||Received("AUDIT135_COMPANY_READY")) && PlayerStore.Instance.playerCash==100000,"company host fixture replicated",60);while(wait.MoveNext())yield return wait.Current;
            var companies=AccessTools.TypeByName("CorpPresence.Companies");var company=AccessTools.Method(companies,"Find").Invoke(null,new object[]{"pastel"});
            Require(!AuditDealer(),"company guest starts with host dealer state");
            NativeClick(0,()=>AccessTools.Method(AccessTools.TypeByName("CorpPresence.Services"),"JoinDealer").Invoke(null,new[]{company}));
            Require(!AuditDealer() && PlayerStore.Instance.playerCash==100000,"company service waits for host without guest deduction");
            wait=Wait(()=>AuditDealer() && PlayerStore.Instance.playerCash<100000,"company cached state and cash replicated from host",60);while(wait.MoveNext())yield return wait.Current;
            RemoteActions.LastNote=null;
            RemoteActions.Forward(RemoteActions.Adapter("CompanyAction",1),null,new object[]{WireJson.ToJson(new CompanyCommand{action="CorpPresence.Services.RequestStamina",args=new[]{"c:pastel"}})});
            wait=Wait(()=>RemoteActions.LastNote==Loc.Text("~SMC125~"),"host refused company service with wrong company",30);while(wait.MoveNext())yield return wait.Current;
            Signal("AUDIT135_COMPANY_DONE");
            string plantText=null;wait=Wait(()=>(plantText=plantText??ReceivePrefix("AUDIT135_PLANT1="))!=null && Find(root,"AUDIT135_PLANT")!=null,"plant analyser fixture arrived",60);while(wait.MoveNext())yield return wait.Current;
            var plant=Find(root,"AUDIT135_PLANT");var analyser=PlantAnalyserUIManager.Instance;analyser.OpenUI(plant);
            wait=Wait(()=>Fingerprint.Hash(analyser.body2TMP.text).ToString()==plantText,"plant analyser first host value displayed",60);while(wait.MoveNext())yield return wait.Current;Signal("AUDIT135_PLANT_OPEN");
            plantText=null;wait=Wait(()=>(plantText=plantText??ReceivePrefix("AUDIT135_PLANT2="))!=null && Fingerprint.Hash(analyser.body2TMP.text).ToString()==plantText,"already-open plant analyser displayed next host value",60);while(wait.MoveNext())yield return wait.Current;Signal("AUDIT135_PLANT_DONE");
        }
    }
}
#endif
