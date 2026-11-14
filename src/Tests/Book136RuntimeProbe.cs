#if LAN_PROBE
using System;
using System.Collections;
using HarmonyLib;
using UnityEngine;
namespace ShopTogether {
 internal static partial class RuntimeProbe {
  static IEnumerator BookHost136(Connection root){
   var item=ModHelper.SpawnItem("ingot_book");GeneralHelper.SetItemOwned(item,true);item.name="BOOK136";EmporiumEntry.Instance.hiddenElement.UncheckedAccept(item);root.World.Index();Signal("BOOK136_READY");
   var wait=Wait(()=>Received("BOOK136_REFRESHED"),"guest native book opens and flips locally",60);while(wait.MoveNext())yield return wait.Current;
   Require(!MultiPageUIManager.Instance.panel.activeSelf&&item.GetTagReadonly("CURRENT_BOOK_PAGE").GetInt()==-1,"personal reading leaves host view and item state unchanged");
   ModHelper.SetModData("player.shop_storage_pages","page_state_v1","{\"unlocked\":2,\"revision\":100,\"names\":[\"\",\"\"]}");
   var pages=MainPages();var saved=AccessTools.Method(AccessTools.TypeByName("ShopStoragePages.LoadPages"),"ReadMetadata").Invoke(null,new object[]{ModHelper.GetModData("player.shop_storage_pages","page_state_v1")});AccessTools.Method(pages.GetType(),"Restore").Invoke(pages,new object[]{saved,null});
   var grid=EmporiumEntry.Instance.invElement;var a=ModHelper.SpawnItem("magnifier");var b=ModHelper.SpawnItem("screwdriver");a.name="BOOK136_SORT_A";b.name="BOOK136_SORT_B";GeneralHelper.SetItemOwned(a,true);GeneralHelper.SetItemOwned(b,true);
   a.modifiedShape=new GridShapeBuilder(a.modifiedShape).SetPosition(4,14).Build();b.modifiedShape=new GridShapeBuilder(b.modifiedShape).SetPosition(9,17).Build();grid.UncheckedAccept(a);grid.UncheckedAccept(b);root.World.Index();
   long lease;int held=root.World.Id(Find(root,"SMC_HOST_ITEM"));Require(root.Leases.Claim(2,new[]{held},Time.realtimeSinceStartup,out lease),"other page held by another player");Signal("BOOK136_SORT_READY");
   wait=Wait(()=>InventorySortHelper.MayUndo(grid),"requested storage page sorted despite another page lease",60);while(wait.MoveNext())yield return wait.Current;
   Require(StoragePagesIntent.Page(grid)==0&&a.modifiedShape.minY>=11&&b.modifiedShape.minY>=11,"sort remains in guest page and preserves host page");root.Leases.Release(2,new[]{held});Signal("BOOK136_SORTED");
   wait=Wait(()=>!InventorySortHelper.MayUndo(grid),"guest page sort undo executed on host",60);while(wait.MoveNext())yield return wait.Current;
   Require(a.modifiedShape.minX==4&&a.modifiedShape.minY==14&&b.modifiedShape.minX==9&&b.modifiedShape.minY==17,"page undo restored exact positions");Signal("BOOK136_UNDONE");
   wait=Wait(()=>Received("BOOK136_DONE"),"book and page sort results returned",60);while(wait.MoveNext())yield return wait.Current;
  }
  static IEnumerator BookClient136(Connection root){
   bool ready=false;var wait=Wait(()=>(ready=ready||Received("BOOK136_READY"))&&Find(root,"BOOK136")!=null,"book item replicated",60);while(wait.MoveNext())yield return wait.Current;
   var item=Find(root,"BOOK136");NativeClick(0,()=>item.ActivateSlotItem());
   wait=Wait(()=>MultiPageUIManager.Instance.panel.activeSelf,"native book panel relayed to guest",60);while(wait.MoveNext())yield return wait.Current;
   var ui=MultiPageUIManager.Instance;
   for(int book=0;book<ui.books.Count;book++){
    ui.OpenUI(book);int pages=ui.books[book].transform.childCount;
    NativeClick(0,()=>ui.FlipRightClicked());int current=(int)AccessTools.Field(ui.GetType(),"currentPage").GetValue(ui);
    Require(current==(pages>1?1:0)&&ui.books[book].transform.GetChild(current).gameObject.activeSelf,"native book next page or single-page bound "+book);
    NativeClick(0,()=>ui.FlipLeftClicked());Require((int)AccessTools.Field(ui.GetType(),"currentPage").GetValue(ui)==0&&ui.books[book].transform.GetChild(0).gameObject.activeSelf,"native book previous page "+book);
   }
   ui.CloseUI();Signal("BOOK136_REFRESHED");
   ready=false;wait=Wait(()=>(ready=ready||Received("BOOK136_SORT_READY"))&&Find(root,"BOOK136_SORT_B")!=null&&MainPages()!=null&&MainPageCount()==2,"second storage page replicated",60);while(wait.MoveNext())yield return wait.Current;
   var grid=EmporiumEntry.Instance.invElement;AccessTools.Field(MainPages().GetType(),"page").SetValue(MainPages(),1);NativeClick(0,()=>InventorySortHelper.Sort(grid,true,true));
   wait=Wait(()=>Received("BOOK136_SORTED"),"native sort used guest selected page",60);while(wait.MoveNext())yield return wait.Current;
   NativeClick(0,()=>InventorySortHelper.Undo(grid));wait=Wait(()=>Received("BOOK136_UNDONE"),"native page undo verified",60);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>Find(root,"BOOK136_SORT_A").modifiedShape.minY==14&&Find(root,"BOOK136_SORT_B").modifiedShape.minY==17,"restored page positions replicated",60);while(wait.MoveNext())yield return wait.Current;
   RemoteActions.LastNote=null;RemoteActions.Forward(RemoteActions.Adapter("Save",2),null,new object[]{item,"{}"});
   wait=Wait(()=>RemoteActions.LastNote==Loc.Text("~SMC305~"),"wrong coordinator source receives refusal",60);while(wait.MoveNext())yield return wait.Current;
   RemoteActions.LastNote=null;RemoteActions.Forward(RemoteActions.Adapter("Fire",0),null,new object[0]);
   wait=Wait(()=>RemoteActions.LastNote==Loc.Text("~SMC303~"),"shooting without aim receives refusal",60);while(wait.MoveNext())yield return wait.Current;
   RemoteActions.LastNote=null;RemoteActions.Forward(RemoteActions.Adapter("VisitorBuy",2),null,new object[]{0L,0});
   wait=Wait(()=>RemoteActions.LastNote==Loc.Text("~SMC281~"),"missing visitor receives refusal",60);while(wait.MoveNext())yield return wait.Current;Signal("BOOK136_DONE");
  }
 }
}
#endif
