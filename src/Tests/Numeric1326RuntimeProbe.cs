#if LAN_PROBE
using System;
using System.Collections;
using System.Text;
using UnityEngine;
namespace ShopTogether {
 internal static partial class RuntimeProbe {
  static string NumericTooltip(GameItem item) { return ((GameItemElement)item).GetTooltip().nakedText; }
  static void NumericSend(string key,GameItem item) {
   string text=NumericTooltip(item);byte[] bytes=Encoding.UTF8.GetBytes(text);
   Require(bytes.Length<650,"tooltip fits independent test control message: "+key);
   Signal(key+"="+Convert.ToBase64String(bytes));
  }
  static IEnumerator NumericMatch(string key,GameItem item,string previous) {
   string encoded=null;var wait=Wait(()=>(encoded=ReceivePrefix(key+"="))!=null,"host numeric tooltip received: "+key,90);while(wait.MoveNext())yield return wait.Current;
   string expected=Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
   wait=Wait(()=>NumericTooltip(item)==expected,"guest displayed numeric tooltip matches host: "+key,90);while(wait.MoveNext())yield return wait.Current;
   if(previous!=null)Require(expected!=previous,"upgrade changes actual displayed tooltip: "+key);
  }
  static IEnumerator NumericHost1326(Connection root) {
   var entry=EmporiumEntry.Instance;entry.invElement.SetShape(36,36);entry.Validate(true);
   var battery=MachineFixture("energy_credit_hyp","NUM1326_BATTERY");
   var reactor=MachineFixture("makeshift_reactor","NUM1326_REACTOR");
   var converter=MachineFixture("current_converter","NUM1326_CONVERTER");
   var blender=MachineFixture("blender","NUM1326_CHEMICAL");
   root.World.Index();Signal("NUM1326_READY");
   var wait=Wait(()=>Received("NUM1326_BASELINE"),"guest numeric fixtures ready",90);while(wait.MoveNext())yield return wait.Current;
   NumericSend("NUM1326_B0",battery);NumericSend("NUM1326_R0",reactor);NumericSend("NUM1326_C0",converter);NumericSend("NUM1326_M0",blender);
   wait=Wait(()=>Received("NUM1326_UPGRADE"),"guest captured initial tooltips",90);while(wait.MoveNext())yield return wait.Current;
   HypertechBatteryHelper.UpgradeOutput(battery,ModHelper.SpawnItem("blank_module"));
   ReactorHelper.Upgrade(reactor,ModHelper.SpawnItem("blank_module"));
   CurrentConverterHelper.UpgradeEfficiency(converter,ModHelper.SpawnItem("blank_module"));
   var module=ModHelper.SpawnItem("chem_module");GeneralHelper.SetItemOwned(module,true);
   var slots=MachineHelper.GetBodyStorage(blender,0) as GameInventory;Require(slots!=null&&GraphUtils.TryAcceptAll(slots,module)==0,"host installed real chemistry module");
   Require(HypertechBatteryHelper.GetOutput(battery)==22&&ReactorHelper.GetUpgrades(reactor)==1&&CurrentConverterHelper.GetUpgradeCount(converter)==1&&MachineBlender.GetLoss(blender)==20,"host native numeric upgrades took effect");
   NumericSend("NUM1326_B1",battery);NumericSend("NUM1326_R1",reactor);NumericSend("NUM1326_C1",converter);NumericSend("NUM1326_M1",blender);
   wait=Wait(()=>Received("NUM1326_DONE"),"guest matched all upgraded numeric displays",90);while(wait.MoveNext())yield return wait.Current;
  }
  static IEnumerator NumericClient1326(Connection root) {
   GameItem battery=null,reactor=null,converter=null,blender=null;
   var wait=Wait(()=>(battery=Find(root,"NUM1326_BATTERY"))!=null&&(reactor=Find(root,"NUM1326_REACTOR"))!=null&&(converter=Find(root,"NUM1326_CONVERTER"))!=null&&(blender=Find(root,"NUM1326_CHEMICAL"))!=null&&Received("NUM1326_READY"),"numeric upgrade fixtures replicated",90);while(wait.MoveNext())yield return wait.Current;
   Signal("NUM1326_BASELINE");
   foreach(var pair in new[]{new {Key="NUM1326_B0",Item=battery},new {Key="NUM1326_R0",Item=reactor},new {Key="NUM1326_C0",Item=converter},new {Key="NUM1326_M0",Item=blender}}) {wait=NumericMatch(pair.Key,pair.Item,null);while(wait.MoveNext())yield return wait.Current;}
   string b=NumericTooltip(battery),r=NumericTooltip(reactor),c=NumericTooltip(converter),m=NumericTooltip(blender);Signal("NUM1326_UPGRADE");
   foreach(var pair in new[]{new {Key="NUM1326_B1",Item=battery,Before=b},new {Key="NUM1326_R1",Item=reactor,Before=r},new {Key="NUM1326_C1",Item=converter,Before=c},new {Key="NUM1326_M1",Item=blender,Before=m}}) {wait=NumericMatch(pair.Key,pair.Item,pair.Before);while(wait.MoveNext())yield return wait.Current;}
   Require(HypertechBatteryHelper.GetOutput(battery)==22&&ReactorHelper.GetUpgrades(reactor)==1&&CurrentConverterHelper.GetUpgradeCount(converter)==1&&MachineBlender.GetLoss(blender)==20,"guest derives upgraded values from replicated items and module");
   Signal("NUM1326_DONE");
  }
 }
}
#endif
