using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Linq;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace ShopTogether {
    [Serializable] internal sealed class CompanyCommand { public string action; public string[] args; }
    [Serializable] internal sealed class CompanyOrderLine { public string id; public int index, count; }
    internal static class CompanyIntent {
        static readonly Dictionary<string,MethodInfo> actions=new Dictionary<string,MethodInfo>();
        static readonly Dictionary<string,string> companyTypes=new Dictionary<string,string>();
        static readonly HashSet<MethodBase> installed=new HashSet<MethodBase>();
        static Harmony patches;
        static Type companyType,companies,window,corpState,maliceState;
        static int drawing;
        static bool awaiting;
        static float nextFlush;
        const string PatchId="shop_together.company-intent";
        static Type T(string name) { return AccessTools.TypeByName("CorpPresence."+name); }
        static object Get(object value,string name) { return value==null?null:AccessTools.Property(value.GetType(),name).GetValue(value,null); }
        static object Call(Type type,string name,params object[] args) { return AccessTools.Method(type,name).Invoke(null,args); }
        static string Id(object company) { return Get(company,"Id") as string; }
        static object Find(string id) { return Call(companies,"Find",id); }
        static object State(object company) { return Call(companies,"StateOf",company); }
        static string Key(MethodBase method) { return method.DeclaringType.FullName+"."+method.Name; }
        static void Patch(MethodBase method,string prefix=null,string postfix=null) {
            if(method==null || installed.Contains(method))return;
            patches.Patch(method,prefix:prefix==null?null:new HarmonyMethod(typeof(CompanyIntent),prefix),postfix:postfix==null?null:new HarmonyMethod(typeof(CompanyIntent),postfix));installed.Add(method);
        }
        internal static void Bind() {
            if(companyType!=null)return;
            var type=T("Company");if(type==null)return;
            companyType=type;companies=T("Companies");window=T("UI.CompanyWindow");corpState=T("CorpState");maliceState=T("Malice.MaliceState");patches=new Harmony(PatchId);
            Add("Services","PlaceOrder",null);
            foreach(string name in new[]{"RequestBloodRecovery","RequestSwap","RequestStamina"})Add("Services",name,"CarmichaelCo");
            Add("Services","RequestRefurbish","MeridianCo");Add("Services","JoinDealer","PastelCo");
            foreach(string name in new[]{"BookVip","BookLab","ClaimGift"})Add("Services",name,"SolanaCo");
            foreach(string name in new[]{"OrderLeviathan","MakeUpCanning","OrderCanningMachine"})Add("Services",name,"DavreCo");
            Add("CarmichaelCo","OrderSpecimen","CarmichaelCo");Add("MeridianCo","OrderForgeMachine","MeridianCo");Add("MeridianCo","MakeUpForge",null);
            foreach(string name in new[]{"PayOffAid","AgreePlan","PayServiceFee","AcceptBomb","BribeForReconcile","ReconcileWithRebels","CallBombOdin"})Add("Malice.Envoy",name,null);
            Add("Malice.Malice","Subscribe",null);Add("Malice.Malice","ApplyUnlock",null);
            Add("Malice.RebelStory","BribeMeridian",null);Add("Malice.RebelStory","ReorderBomb",null);
            Patch(AccessTools.Method(T("Malice.MalicePanel"),"Say"),"Toast");
            patches.Patch(AccessTools.Method(T("Malice.MalicePanel"),"Draw"),transpiler:new HarmonyMethod(typeof(CompanyIntent),"MaliceControls"));
            Patch(AccessTools.PropertySetter(T("Malice.CorpTerms"),"AutoRenew"),"AutoRenew");
            Add("Malice.Rebels","BookFence",null);Add("Malice.RebelStory","CallPickup",null);Add("Malice.RebelStory","CallOdin",null);
            Patch(AccessTools.Method(window,"Open"),"OpenCompany");Patch(AccessTools.Method(T("CardView"),"Open"),"OpenCard");
            Patch(AccessTools.Method(window,"Draw"),"BeginDraw","EndDraw");Patch(AccessTools.Method(window,"Toast"),"Toast");
            Patch(AccessTools.Method(T("UI.Ui"),"Button"),"BeginButton","EndButton");
            Patch(AccessTools.Method(T("UI.Ui"),"Qty"),"QuantityIntent");
            Patch(AccessTools.PropertySetter(T("CompanyState"),"TrafficOn"),"Traffic");
            Patch(AccessTools.Method(T("PastelCo"),"PayBribe"),"Bribe");
            Patch(AccessTools.Method(T("Services"),"Pay"),"BeforePayment");
            Patch(AccessTools.Method(T("Malice.Rebels"),"Charge"),"BeforeRebelPayment");
            Patch(AccessTools.Method(T("Malice.Malice"),"Charge"),"BeforePayment");
            foreach(string name in new[]{"PayOffAid","PayServiceFee","BribeForReconcile","ReconcileWithRebels"})PatchCashAction(AccessTools.Method(T("Malice.Envoy"),name));
            PatchCashAction(AccessTools.Method(T("Malice.RebelStory"),"OrderBomb"));
            Patch(AccessTools.Method(corpState,"Save"),"HostSave");Patch(AccessTools.Method(maliceState,"Save"),"HostSave");
            foreach(string name in new[]{"DrawCarmichael","DrawCarmStamina","DrawCarmSpecimen","DrawDavre","DrawDavreCanning","DrawDavreLeviathan","DrawMeridian","DrawMeridianForge","DrawMeridianRefurb","DrawSolana","DrawSolanaVip","DrawSolanaLab","DrawSpacer","DrawSpacerMutual","DrawPastelOrder","DrawCarmOrder","DrawDavreOrder","DrawEquipmentOrder","DrawSpacerOrder"})
                patches.Patch(AccessTools.Method(window,name),transpiler:new HarmonyMethod(typeof(CompanyIntent),"ClientDisplayGates"));
            Patch(AccessTools.Method(window,"PastelUnlocked"),postfix:"ClientUnlocked");
        }
        static void Add(string type,string name,string expectedCompany) {
            var method=AccessTools.Method(T(type),name);if(method==null)throw new InvalidOperationException("Missing company action: "+type+"."+name);
            if(method.ReturnType!=typeof(string))throw new InvalidOperationException("Unexpected company action result");
            actions.Add(Key(method),method);companyTypes.Add(Key(method),expectedCompany);Patch(method,"Propose");
        }
        internal static void Shutdown() { if(patches!=null)patches.UnpatchAll(PatchId);patches=null;installed.Clear();actions.Clear();companyTypes.Clear();companyType=null;drawing=0; }
        static bool HostSave() { return !ReplicaSession.ClientStore; }
        static bool ClientDrawing(){return ReplicaSession.ClientStore&&drawing>0;}
        static bool ClientHint(bool value){return value||ClientDrawing();}
        static bool ClientBlocked(bool value){return value&&!ClientDrawing();}
        static int ClientTier(int value){return ClientDrawing()?int.MaxValue:value;}
        static string ClientOrderError(string value){return ClientDrawing()?null:value;}
        static void QuantityIntent(ref int max){if(ClientDrawing())max=Math.Max(10,max);}
        static void ClientUnlocked(ref bool __result){if(ClientDrawing())__result=true;}
        static IEnumerable<CodeInstruction> MaliceControls(IEnumerable<CodeInstruction> instructions) {
            var code=instructions.ToList();var applyExits=new HashSet<Label>();
            for(int i=0;i<code.Count;i++) {
                var instruction=code[i];
                // Change action gates only; displayed prices and prerequisites
                // still read the host's synchronized values.
                if((instruction.opcode==OpCodes.Brfalse||instruction.opcode==OpCodes.Brfalse_S)&&i>0) {
                    var condition=code[i-1].operand as MethodInfo;
                    if(condition!=null&&(condition.Name=="get_Ch2BribeOpen"||condition.Name=="get_Ch2ReorderOpen")) {
                        var hint=new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(CompanyIntent),"ClientHint"));hint.labels.AddRange(instruction.labels);instruction.labels.Clear();yield return hint;
                    }
                }
                if(instruction.operand is Label&&(instruction.opcode==OpCodes.Bne_Un||instruction.opcode==OpCodes.Bne_Un_S||instruction.opcode==OpCodes.Blt||instruction.opcode==OpCodes.Blt_S)) {
                    var target=(Label)instruction.operand;bool unlocked=false;
                    for(int j=Math.Max(0,i-5);j<i;j++){var m=code[j].operand as MethodInfo;if(m!=null&&m.Name=="get_Unlocked")unlocked=true;}
                    if(unlocked)applyExits.Add(target);
                    if(applyExits.Contains(target)) {
                        bool unequal=instruction.opcode==OpCodes.Bne_Un||instruction.opcode==OpCodes.Bne_Un_S;
                        var compare=new CodeInstruction(unequal?OpCodes.Ceq:OpCodes.Clt);compare.labels.AddRange(instruction.labels);instruction.labels.Clear();yield return compare;
                        yield return new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(CompanyIntent),unequal?"ClientHint":"ClientBlocked"));
                        instruction.opcode=unequal?OpCodes.Brfalse:OpCodes.Brtrue;
                    }
                }
                yield return instruction;
            }
        }
        static IEnumerable<CodeInstruction> ClientDisplayGates(IEnumerable<CodeInstruction> source,MethodBase __originalMethod){
            var code=source.ToList();var locked=new HashSet<string>{"ui.carmichael.overview.shopNotRegisteredYetDeal","ui.carmichael.stamina.thisNewProductHasnT","ui.davre.overview.noSupplierNumberYet","ui.davre.canning.doreenWillComeTalkOnce","ui.davre.purifier.davreSOwnWaterPurifier","ui.meridian.overview.noLicense","ui.meridian.forging.rosenWillComeTeachOnce","ui.meridian.refurbishment.tierRequired","ui.solana.overview.tierExclusive","ui.spacer.mutualAid.tierRequired"};
            for(int i=0;i<code.Count;i++){
                var instruction=code[i];
                if(__originalMethod.Name=="DrawSolanaVip"&&i>1&&(instruction.opcode==OpCodes.Brfalse||instruction.opcode==OpCodes.Brfalse_S)&&code[i-1].IsLdloc())
                    yield return new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(CompanyIntent),"ClientHint"));
                yield return instruction;
                var called=instruction.operand as MethodInfo;
                if(called!=null&&(instruction.opcode==OpCodes.Call||instruction.opcode==OpCodes.Callvirt)){
                    if((__originalMethod.Name=="DrawCarmSpecimen"&&(called.Name=="get_Licensed"||called.Name=="get_ReactorOpen"))||
                       (__originalMethod.Name=="DrawPastelOrder"&&(called.Name=="HemingwayOpen"||called.Name=="CraftUnlocked")))
                        yield return new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(CompanyIntent),"ClientHint"));
                    if(__originalMethod.Name=="DrawSolanaLab"&&called.Name=="get_LabBooked")
                        yield return new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(CompanyIntent),"ClientBlocked"));
                    if(__originalMethod.Name=="DrawSpacer"&&called.Name=="Tier")
                        yield return new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(CompanyIntent),"ClientTier"));
                    if((called.Name=="OrderEntryError"||called.Name=="CarmLocked")&&called.ReturnType==typeof(string))
                        yield return new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(CompanyIntent),"ClientOrderError"));
                }
                if(instruction.opcode.FlowControl!=FlowControl.Cond_Branch||!(instruction.operand is Label))continue;
                var label=(Label)instruction.operand;int end=code.FindIndex(i+1,x=>x.labels.Contains(label));if(end<=i||end-i>130)continue;
                bool gate=false;for(int j=i+1;j<end;j++)if(code[j].opcode==OpCodes.Ldstr&&locked.Contains(code[j].operand as string)){gate=true;break;}
                if(gate){yield return new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(CompanyIntent),"ClientDrawing"));yield return new CodeInstruction(OpCodes.Brtrue,label);}
            }
        }
        internal static void Flush(bool force=false) { if(companyType==null || ReplicaSession.ClientStore || PlayerStore.Instance==null || (!force && Time.realtimeSinceStartup<nextFlush))return;nextFlush=Time.realtimeSinceStartup+.5f;Call(corpState,"Save");if(maliceState!=null)Call(maliceState,"Save"); }
        static void BeginDraw() { drawing++;awaiting=false; }
        static void EndDraw() { drawing--;awaiting=false; }
        static void BeginButton(out bool __state) { __state=GUI.enabled;if(ReplicaSession.ClientStore && drawing>0)GUI.enabled=true; }
        static void EndButton(bool __state) { GUI.enabled=__state; }
        static bool Toast() { return !ReplicaSession.ClientStore || !awaiting; }
        static bool Propose(MethodBase __originalMethod,object[] __args,ref string __result) {
            bool localHost=!ReplicaSession.ClientStore;
            if(localHost){var session=Connection.Current;if(session==null||!session.IsHost||!session.Ready||WorldReplica.Applying||RemoteActions.CurrentOwner>=0||NativeActions.Executing||(drawing<=0&&!LocalControl.UserAction))return true;}
            __result=Loc.Text("~SMC340~");awaiting=true;
            if(drawing<=0 && !LocalControl.UserAction)return false;
            var values=new List<string>();
            foreach(var arg in __args) {
                if(arg!=null && companyType.IsInstanceOfType(arg))values.Add("c:"+Id(arg));
                else if(arg is IDictionary) {
                    object company=__args[0];var catalog=Get(company,"Catalog") as Array;var lines=new List<CompanyOrderLine>();
                    foreach(DictionaryEntry pair in (IDictionary)arg) { int index=-1;for(int i=0;i<catalog.Length;i++)if(ReferenceEquals(catalog.GetValue(i),pair.Key)){index=i;break;}
                        lines.Add(new CompanyOrderLine{id=(string)AccessTools.Field(pair.Key.GetType(),"Id").GetValue(pair.Key),index=index,count=(int)pair.Value}); }
                    values.Add("o:"+WireJson.ToJson(lines.ToArray()));
                } else if(arg is Delegate)values.Add("p:");
                else if(arg is int)values.Add("i:"+((int)arg).ToString(CultureInfo.InvariantCulture));
                else if(arg is string)values.Add("s:"+(string)arg);
                else { RemoteActions.Notice("~SMC305~");return false; }
            }
            var command=new CompanyCommand{action=Key(__originalMethod),args=values.ToArray()};
            if(localHost){
                int previousOwner=RemoteActions.CurrentOwner;string previousNote=RemoteActions.PendingNote;
                RemoteActions.CurrentOwner=0;RemoteActions.PendingNote=null;
                try{CompanyAction(WireJson.ToJson(command));string note=RemoteActions.PendingNote;__result=note=="~SMC341~"?null:Loc.Text(note??"~SMC321~");}
                finally{RemoteActions.CurrentOwner=previousOwner;RemoteActions.PendingNote=previousNote;awaiting=false;}
            }else Send(command);
            return false;
        }
        static void Send(CompanyCommand command) {
            var s=Connection.Current;if(s==null || !s.Ready){RemoteActions.Notice("~SMC242~");return;}
            string packed=WireJson.ToJson(command);if(packed.Length>16000){RemoteActions.Notice("~SMC305~");return;}
            RemoteActions.Forward(RemoteActions.Adapter("CompanyAction",1),null,new object[]{packed});
        }
        static bool Traffic(object __instance,bool __0) {
            if(!ReplicaSession.ClientStore)return true;
            if(drawing>0){var company=AccessTools.Field(window,"_company").GetValue(null);awaiting=true;Send(new CompanyCommand{action="Traffic",args=new[]{Id(company),__0?"1":"0"}});}
            return WorldReplica.Applying;
        }
        static bool AutoRenew(object __instance,bool __0) {
            if(!ReplicaSession.ClientStore || WorldReplica.Applying)return true;
            if(drawing>0){
                var company=AccessTools.Field(window,"_company").GetValue(null);
                if(company!=null && ReferenceEquals(Call(T("Malice.Malice"),"TermsOf",company),__instance)) {
                    awaiting=true;Send(new CompanyCommand{action="AutoRenew",args=new[]{Id(company),__0?"1":"0"}});
                }
            }
            return false;
        }
        static bool Bribe(bool __0,ref bool __result) {
            if(!ReplicaSession.ClientStore)return true;__result=false;
            if(drawing>0 || LocalControl.UserAction){awaiting=true;Send(new CompanyCommand{action="PastelBribe",args=new[]{__0?"1":"0"}});}return false;
        }
        // Apply the cash reservation at the native fallible payment boundary.
        // Free gifts, incoming payments and non-paying phone choices stay usable.
        static bool BeforePayment(object __0,int __1,ref string __result) {
            if(ReplicaSession.ClientStore || __1<=0 || (int)Call(T("Services"),"CashDue",__0,__1)<=0 || DialoguePayment.AllowSpending())return true;
            __result=Loc.Text(RemoteActions.PendingNote);return false;
        }
        static bool BeforeRebelPayment(int __0,ref string __result) {
            if(ReplicaSession.ClientStore || __0<=0 || DialoguePayment.AllowSpending())return true;
            __result=Loc.Text(RemoteActions.PendingNote);return false;
        }
        static void PatchCashAction(MethodBase method){patches.Patch(method,prefix:new HarmonyMethod(typeof(CompanyIntent),"BeforeCashAction"));}
        static bool BeforeCashAction(ref string __result){if(DialoguePayment.AllowSpending())return true;__result=Loc.Text(RemoteActions.PendingNote);return false;}
        internal static void CompanyAction(string packed) {
            Bind();var command=WireJson.FromJson<CompanyCommand>(packed);
            if(command==null || command.args==null || command.args.Length>4 || companyType==null){RemoteActions.PendingNote="~SMC305~";return;}
            if(command.action=="PastelBribe") {
                if(command.args.Length!=1 || command.args[0]!="1"){RemoteActions.PendingNote="~SMC305~";return;}
                var c=Find("pastel");var state=State(c);
                if(state==null || (bool)Get(state,"Bribed")){RemoteActions.PendingNote="~SMC342~";return;}
                int price=(int)AccessTools.Property(T("PastelCo"),"Bribe").GetValue(null,null);
                int cash=(int)Call(T("Services"),"CashDue",c,price);
                if(!(bool)Call(T("Services"),"CanAfford",cash)){RemoteActions.PendingNote="~SMC343~";return;}
                bool paid=(bool)Call(T("PastelCo"),"PayBribe",true);Flush(true);RemoteActions.PendingNote=paid?"~SMC341~":"~SMC125~";return;
            }
            if(command.action=="AutoRenew") {
                if(command.args.Length!=2 || (command.args[1]!="0" && command.args[1]!="1")){RemoteActions.PendingNote="~SMC305~";return;}
                var c=Find(command.args[0]);
                if(c==null || !(bool)Get(c,"Active") || !(bool)Call(T("Malice.Malice"),"Applies",c)){RemoteActions.PendingNote="~SMC345~";return;}
                var terms=Call(T("Malice.Malice"),"TermsOf",c);
                if(terms==null){RemoteActions.PendingNote="~SMC326~";return;}
                AccessTools.Property(terms.GetType(),"AutoRenew").SetValue(terms,command.args[1]=="1",null);
                Flush(true);RemoteActions.PendingNote="~SMC341~";return;
            }
            if(command.action=="Traffic") {
                if(command.args.Length!=2 || (command.args[1]!="0" && command.args[1]!="1")){RemoteActions.PendingNote="~SMC305~";return;}
                var c=Find(command.args[0]);if(c==null || (c.GetType()!=T("PastelCo") && c.GetType()!=T("SpacerCo")) || !(bool)Get(c,"Active")){RemoteActions.PendingNote="~SMC125~";return;}
                if(command.args[1]=="1"&&(c.GetType()==T("PastelCo")?!(bool)Get(State(c),"DealerJoined"):Tier(c)<2)){RemoteActions.PendingNote="~SMC345~";return;}
                AccessTools.Property(State(c).GetType(),"TrafficOn").SetValue(State(c),command.args[1]=="1",null);Flush(true);RemoteActions.PendingNote="~SMC341~";return;
            }
            MethodInfo method;if(!actions.TryGetValue(command.action??"",out method) || method.GetParameters().Length!=command.args.Length){RemoteActions.PendingNote="~SMC305~";return;}
            var args=new object[command.args.Length];object company=null;var parameters=method.GetParameters();
            for(int i=0;i<args.Length;i++) {
                string value=command.args[i];if(value==null || value.Length<2 || value[1]!=':'){RemoteActions.PendingNote="~SMC305~";return;}
                string raw=value.Substring(2);Type want=parameters[i].ParameterType;
                if(companyType.IsAssignableFrom(want) && value[0]=='c') { company=Find(raw);if(company==null || !want.IsInstanceOfType(company) || !(bool)Get(company,"Active") || (companyTypes[command.action]!=null && company.GetType()!=T(companyTypes[command.action]))){RemoteActions.PendingNote="~SMC125~";return;}args[i]=company; }
                else if(want==typeof(int) && value[0]=='i'){int number;if(!int.TryParse(raw,out number)){RemoteActions.PendingNote="~SMC305~";return;}args[i]=number;}
                else if(want==typeof(string) && value[0]=='s')args[i]=raw;
                else if(typeof(IDictionary).IsAssignableFrom(want) && value[0]=='o' && company!=null) {
                    var lines=WireJson.FromJson<CompanyOrderLine[]>(raw);var catalog=Get(company,"Catalog") as Array;
                    if(lines==null || lines.Length==0 || lines.Length>128 || catalog==null){RemoteActions.PendingNote="~SMC305~";return;}
                    var cart=(IDictionary)Activator.CreateInstance(want);
                    foreach(var line in lines) { if(line==null || line.count<1 || line.count>1000 || line.index<0 || line.index>=catalog.Length){RemoteActions.PendingNote="~SMC305~";return;}
                        var entry=catalog.GetValue(line.index);if((string)AccessTools.Field(entry.GetType(),"Id").GetValue(entry)!=line.id || cart.Contains(entry)){RemoteActions.PendingNote="~SMC305~";return;}cart.Add(entry,line.count); }
                    args[i]=cart;
                } else if(typeof(Delegate).IsAssignableFrom(want) && value[0]=='p' && company!=null) {
                    object c=company;args[i]=new Func<object,int>(entry=>(int)AccessTools.Method(companyType,"OrderUnitPrice").Invoke(c,new[]{entry,State(c)}));
                } else {RemoteActions.PendingNote="~SMC305~";return;}
            }
            // Prices are host quotes, never accepted from a remote argument.
            if(company!=null){int minimum=method.Name=="BookLab"?2:method.Name=="ClaimGift"?3:method.Name=="RequestRefurbish"?2:
                method.Name=="Subscribe"||method.Name=="ApplyUnlock"||method.Name=="PlaceOrder"||method.Name=="OrderSpecimen"||method.Name=="RequestBloodRecovery"||method.Name=="OrderForgeMachine"?0:1;
                if(Tier(company)<minimum){RemoteActions.PendingNote="~SMC345~";return;}}
            if(method.DeclaringType==T("Malice.Malice") && (company==null || !(bool)Call(T("Malice.Malice"),"Applies",company))){RemoteActions.PendingNote="~SMC345~";return;}
            if(method.Name=="OrderLeviathan")args[1]=Call(window,"LeviathanPrice");
            if(method.Name=="OrderSpecimen") {
                var prices=new Dictionary<string,int>{{"@silverblood",500},{"@blood_drawer",250},{"@isomer_reactor",400},{"@sb_handbook",50}};int price;
                if(!prices.TryGetValue((string)args[1],out price)){RemoteActions.PendingNote="~SMC305~";return;}args[2]=price;
            }
            if(method.Name=="OrderCanningMachine" || method.Name=="OrderForgeMachine") {
                Type kind=T(method.Name=="OrderCanningMachine"?"DavreCo":"MeridianCo");string field=method.Name=="OrderCanningMachine"?"CanningMachines":"ForgeMachines";
                string goods=null;foreach(var pair in (IEnumerable)AccessTools.Field(kind,field).GetValue(null))if((string)AccessTools.Field(pair.GetType(),"Item1").GetValue(pair)==(string)args[1])goods=(string)AccessTools.Field(pair.GetType(),"Item2").GetValue(pair);
                if(goods==null){RemoteActions.PendingNote="~SMC305~";return;}args[2]=checked((int)Convert.ToInt64(Call(T("Goods"),"ValueOf",goods)));
            }
            // These services consume a selected native candidate list, not the whole shop.
            string candidates=method.Name=="RequestBloodRecovery"?"BloodRecoverySamples":method.Name=="RequestSwap"?"FakesOwned":method.Name=="RequestRefurbish"?"RefurbishCandidates":null;
            if(candidates!=null){
                int limit=method.Name=="RequestSwap"?6:method.Name=="RequestRefurbish"?3:int.MaxValue;
                foreach(GameItem item in ((IEnumerable)Call(T("Services"),candidates)).Cast<GameItem>().Take(limit))
                    if(!DialogueIntent.MayChange(Connection.Current,item) || !AuthorityIntent.FreeTree(Connection.Current,Math.Max(0,RemoteActions.CurrentOwner),item))return;
            }
            string result;using(HostNativeScope.Begin())result=(string)method.Invoke(null,args);Flush(true);RemoteActions.PendingNote=string.IsNullOrEmpty(result)?"~SMC341~":result;
        }
        static bool OpenCompany(object __0) { if(!PanelRelay.Capturing)return true;Relay(new PanelCall{m=52,texts=new[]{Id(__0)}});return false; }
        static int Tier(object company){return (int)AccessTools.Method(companyType,"Tier").Invoke(company,new[]{State(company)});}
        static bool OpenCard(GameItem __0) { if(!PanelRelay.Capturing)return true;Relay(new PanelCall{m=53,items=new[]{Connection.Current.World.Id(__0)}});return false; }
        static void Relay(PanelCall call) { Flush(true);Connection.Current.SendTo(PanelRelay.Owner,PanelRelay.Message,Encoding.UTF8.GetBytes(WireJson.ToJson(call)));PanelRelay.NotifiedDuringAction=true; }
        internal static void Show(int kind,string id,GameItem item) { Bind();if(companyType==null)return;if(kind==52)Call(window,"Open",Find(id));else if(item!=null)Call(T("CardView"),"Open",item); }
    }
}
