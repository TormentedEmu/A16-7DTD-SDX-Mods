using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using SDX.Compiler;
using SDX.Core;

public class MinibikeOpenUI : IPatcherMod
{
   public bool Patch(ModuleDefinition module)
   {
      Logging.LogInfo("Start patch process...");
      ModifyAssembly(module);
      Logging.LogInfo("Patch mod complete.");
      return true;

   }

   private void ModifyAssembly(ModuleDefinition module)
   {
      if (!ModifyPlayerMoveControllerMethod(module) || !ModifyWindowSelectorMethod(module))
            throw new Exception("Failed to modify all required methods!");
   }

   private bool ModifyPlayerMoveControllerMethod(ModuleDefinition module)
   {      
      //var playerMoveControllerNestedClass = module.Types.First(c => c.Name == "PlayerMoveController").NestedTypes.First(n => n.Name == "WN"); // "WN" as of 16.4 (b8)
      var playerMoveControllerNestedTypes = module.Types.First(c => c.Name == "PlayerMoveController").NestedTypes;
      int delNextLines = 0;
      MethodDefinition method = null;

      //Logging.LogInfo("playerMoveControllerNestedTypes: " + playerMoveControllerNestedTypes.Count.ToString());
      foreach (var t in playerMoveControllerNestedTypes)
      {
         //Logging.LogInfo(string.Format("Type: {0}, isClass: {1}, isSealed: {2}, isNestedPrivate: {3}", t.Name, t.IsClass, t.IsSealed, t.IsNestedPrivate));
         var methods = t.Methods;
         if (methods == null)
            continue;

         var movenext = methods.FirstOrDefault(x => x.Name == "MoveNext"); // look for MoveNext since we know the class we're looking for doesn't have one
         if (movenext != null)
            continue;

         var fields = t.Fields;
         if (fields == null)
            continue;

         bool found = false;
         foreach (var f in fields)
         {
            //Logging.LogInfo(string.Format("     Field: {0}, FullName: {1}, FieldType name: {2}", f.Name, f.FullName, f.FieldType.FullName));
            if (f.FieldType.FullName == "NGuiAction/IsEnabledDelegate")
            {
               found = true;
               break;
            }
         }

         if (!found)
            continue;

         foreach (var m in methods)
         {
            //Logging.LogInfo(string.Format("     Method: {0}, FullName: {1}", m.Name, m.FullName));

            var body = m.Body;
            if (body != null && body.Instructions != null && body.Instructions.Count > 0)
            {
               /*
               foreach (var op in body.Instructions)
               {
                  Logging.LogInfo(string.Format("     OpCode: {0}, Operand: {1}", op.OpCode, op.Operand));

                  if (op.OpCode == OpCodes.Ldfld && op.Operand.ToString() == "Entity Entity::AttachedToEntity"
                     && op.Next.OpCode == OpCodes.Ldnull
                     && (op.Next.Next.OpCode == OpCodes.Call && op.Next.Next.Operand.ToString().Contains("op_Inequality")))
                  {
                     Logging.LogInfo(string.Format("Found code to remove at pos: {0}", op.Offset));
                  }
               }
               */

               Instruction eAttachedToEntity = body.Instructions.FirstOrDefault(i => i.OpCode == OpCodes.Ldfld && i.Operand.ToString() == "Entity Entity::AttachedToEntity");
               if (eAttachedToEntity != null && eAttachedToEntity.OpCode == OpCodes.Ldfld && eAttachedToEntity.Next.OpCode == OpCodes.Ldnull && eAttachedToEntity.Next.Next.Operand.ToString().Contains("op_Inequality"))
               {
                  Instruction isVehicle = body.Instructions.FirstOrDefault(i => i.OpCode == OpCodes.Isinst && i.Operand.ToString() == "EntityVehicle");
                  if (isVehicle != null && isVehicle.OpCode == OpCodes.Isinst && isVehicle.Previous.OpCode == OpCodes.Ldfld && isVehicle.Previous.Operand.ToString() == "Entity Entity::AttachedToEntity")
                  {
                     Logging.LogInfo(string.Format("Found first method to modify: {0}", m));
                     method = m;
                     break;
                  }
               }
            }
         }

         if (method != null)
            break;
      }

      if (method != null)
      {
         bool found1 = false, found2 = false;
         var instructions = method.Body.Instructions;
         foreach (var inst in instructions.Reverse())
         {
            if (inst.OpCode == OpCodes.Brtrue_S && inst.Previous.OpCode == OpCodes.Isinst && inst.Previous.Operand.ToString() == "EntityVehicle"
               && inst.Previous.Previous.OpCode == OpCodes.Ldfld && inst.Previous.Previous.Operand.ToString() == "Entity Entity::AttachedToEntity")
            {
               Logging.LogInfo(string.Format("Removing OpCode: {0} Operand: {1}", inst.OpCode, inst.Operand));
               instructions.Remove(inst);
               delNextLines = 5;
               found1 = true;
               continue;
            }
            else if (inst.OpCode == OpCodes.Brfalse_S && inst.Previous.OpCode == OpCodes.Call && inst.Previous.Operand.ToString().Contains("UnityEngine.Object::op_Inequality")
               && inst.Previous.Previous.OpCode == OpCodes.Ldnull && inst.Previous.Previous.Previous.OpCode == OpCodes.Ldfld
               && inst.Previous.Previous.Previous.Operand.ToString() == "Entity Entity::AttachedToEntity")
            {
               Logging.LogInfo(string.Format("Removing OpCode: {0} Operand: {1}", inst.OpCode, inst.Operand));
               instructions.Remove(inst);
               delNextLines = 6;
               found2 = true;
               continue;
            }

            if (delNextLines > 0)
            {
               Logging.LogInfo(string.Format("Removing OpCode: {0} Operand: {1}", inst.OpCode, inst.Operand));
               instructions.Remove(inst);
               delNextLines--;
            }
         }

         if (found1 && found2)
            return true;
      }

      Logging.LogError("Failed to locate the method and code to modify in PlayerMoveController.");
      return false;

      #region IL code
      /*
 
       PlayerMoveController.WN::GZ() as of a16.4 (b8)

       Obfuscated:

         0	0000	ldarg.0
         1	0001	ldfld	class PlayerMoveController PlayerMoveController/WN::ZZ
         2	0006	ldfld	class EntityPlayerLocal PlayerMoveController::entityPlayerLocal
         3	000B	ldc.i4.0
         4	000C	callvirt	instance void EntityAlive::set_AimingGun(bool)
         5	0011	ldarg.0
         6	0012	ldfld	class PlayerMoveController PlayerMoveController/WN::ZZ
         7	0017	ldfld	class GUIWindowManager PlayerMoveController::windowManager
         8	001C	ldc.i4	0x3124D
         9	0021	call	string A.US::ZZ(int32)
         10	0026	dup
         11	0027	pop
         12	0028	callvirt	instance bool GUIWindowManager::IsWindowOpen(string)
         13	002D	brfalse.s	48 (009F) ldarg.0 
         14	002F	ldc.i4.2
         15	0030	switch	[14 (002F)]
         16	0039	ldc.i4.1
         17	003A	brtrue.s	20 (0042) ldarg.0 
         18	003C	ldtoken	instance void PlayerMoveController/WN::GZ()
         19	0041	pop
         20	0042	ldarg.0
         21	0043	ldfld	class PlayerMoveController PlayerMoveController/WN::ZZ
         22	0048	ldfld	class GUIWindowManager PlayerMoveController::windowManager
         23	004D	ldnull
         24	004E	callvirt	instance bool GUIWindowManager::CloseAllOpenWindows(class GUIWindow)
         25	0053	pop
         26	0054	ldarg.0
         27	0055	ldfld	class PlayerMoveController PlayerMoveController/WN::ZZ
         28	005A	ldfld	class GUIWindowManager PlayerMoveController::windowManager
         29	005F	ldc.i4	0x3124D
         30	0064	call	string A.US::ZZ(int32)
         31	0069	dup
         32	006A	pop
         33	006B	callvirt	instance bool GUIWindowManager::IsWindowOpen(string)
         34	0070	dup
         35	0071	pop
         36	0072	brfalse.s	47 (009A) br 95 (0135)
         37	0074	ldc.i4.1
         38	0075	switch	[37 (0074)]
         39	007E	ldarg.0
         40	007F	ldfld	class PlayerMoveController PlayerMoveController/WN::ZZ
         41	0084	ldfld	class GUIWindowManager PlayerMoveController::windowManager
         42	0089	ldc.i4	0x3124D
         43	008E	call	string A.US::ZZ(int32)
         44	0093	dup
         45	0094	pop
         46	0095	callvirt	instance void GUIWindowManager::Close(string)
         47	009A	br	95 (0135) ret 
         48	009F	ldarg.0
         49	00A0	ldfld	class PlayerMoveController PlayerMoveController/WN::ZZ
         50	00A5	ldfld	class GUIWindowManager PlayerMoveController::windowManager
         51	00AA	ldnull
         52	00AB	callvirt	instance bool GUIWindowManager::CloseAllOpenWindows(class GUIWindow)
         53	00B0	dup
         54	00B1	pop
         55	00B2	pop

            56	00B3	ldarg.0
            57	00B4	ldfld	class PlayerMoveController PlayerMoveController/WN::ZZ
            58	00B9	ldfld	class EntityPlayerLocal PlayerMoveController::entityPlayerLocal
            59	00BE	ldfld	class Entity Entity::AttachedToEntity
            60	00C3	ldnull
            61	00C4	call	bool [UnityEngine]UnityEngine.Object::op_Inequality(class [UnityEngine]UnityEngine.Object, class [UnityEngine]UnityEngine.Object)
            62	00C9	dup
            63	00CA	pop
            64	00CB	brfalse.s	76 (00FA) ldarg.0 
            65	00CD	ldc.i4.2
            66	00CE	switch	[65 (00CD)]
            67	00D7	ldarg.0
            68	00D8	ldfld	class PlayerMoveController PlayerMoveController/WN::ZZ
            69	00DD	ldfld	class EntityPlayerLocal PlayerMoveController::entityPlayerLocal
            70	00E2	ldfld	class Entity Entity::AttachedToEntity
            71	00E7	isinst	EntityVehicle
            72	00EC	brfalse.s	76 (00FA) ldarg.0 
            73	00EE	ldc.i4.6
            74	00EF	switch	[73 (00EE)]
            75	00F8	br.s	95 (0135) ret 

         76	00FA	ldarg.0
         77	00FB	ldfld	class PlayerMoveController PlayerMoveController/WN::ZZ
         78	0100	ldfld	class GUIWindowManager PlayerMoveController::windowManager
         79	0105	ldc.i4	0x45684
         80	010A	call	string A.US::ZZ(int32)
         81	010F	dup
         82	0110	pop
         83	0111	ldc.i4.1
         84	0112	ldc.i4.1
         85	0113	ldc.i4.1
         86	0114	callvirt	instance void GUIWindowManager::Open(string, bool, bool, bool)
         87	0119	ldarg.0
         88	011A	ldfld	class PlayerMoveController PlayerMoveController/WN::ZZ
         89	011F	ldfld	class LocalPlayerUI PlayerMoveController::playerUI
         90	0124	callvirt	instance class XUi LocalPlayerUI::get_xui()
         91	0129	dup
         92	012A	pop
         93	012B	ldfld	class XUiC_Radial XUi::RadialWindow
         94	0130	callvirt	instance void XUiC_Radial::SetupMenuData()
         95	0135	ret

      Deobfuscated:

         0	0000	ldarg.0
         1	0001	ldfld	class PlayerMoveController PlayerMoveController/WN::ZZ
         2	0006	ldfld	class EntityPlayerLocal PlayerMoveController::entityPlayerLocal
         3	000B	ldc.i4.0
         4	000C	callvirt	instance void EntityAlive::set_AimingGun(bool)
         5	0011	ldarg.0
         6	0012	ldfld	class PlayerMoveController PlayerMoveController/WN::ZZ
         7	0017	ldfld	class GUIWindowManager PlayerMoveController::windowManager
         8	001C	ldstr	"windowpaging"
         9	0021	callvirt	instance bool GUIWindowManager::IsWindowOpen(string)
         10	0026	brfalse.s	29 (006B) ldarg.0 
         11	0028	ldarg.0
         12	0029	ldfld	class PlayerMoveController PlayerMoveController/WN::ZZ
         13	002E	ldfld	class GUIWindowManager PlayerMoveController::windowManager
         14	0033	ldnull
         15	0034	callvirt	instance bool GUIWindowManager::CloseAllOpenWindows(class GUIWindow)
         16	0039	pop
         17	003A	ldarg.0
         18	003B	ldfld	class PlayerMoveController PlayerMoveController/WN::ZZ
         19	0040	ldfld	class GUIWindowManager PlayerMoveController::windowManager
         20	0045	ldstr	"windowpaging"
         21	004A	callvirt	instance bool GUIWindowManager::IsWindowOpen(string)
         22	004F	brfalse	62 (00DE) ret 
         23	0054	ldarg.0
         24	0055	ldfld	class PlayerMoveController PlayerMoveController/WN::ZZ
         25	005A	ldfld	class GUIWindowManager PlayerMoveController::windowManager
         26	005F	ldstr	"windowpaging"
         27	0064	callvirt	instance void GUIWindowManager::Close(string)
         28	0069	br.s	62 (00DE) ret 
         29	006B	ldarg.0
         30	006C	ldfld	class PlayerMoveController PlayerMoveController/WN::ZZ
         31	0071	ldfld	class GUIWindowManager PlayerMoveController::windowManager
         32	0076	ldnull
         33	0077	callvirt	instance bool GUIWindowManager::CloseAllOpenWindows(class GUIWindow)
         34	007C	pop

            // if statement to modify
            35	007D	ldarg.0
            36	007E	ldfld	class PlayerMoveController PlayerMoveController/WN::ZZ
            37	0083	ldfld	class EntityPlayerLocal PlayerMoveController::entityPlayerLocal
            38	0088	ldfld	class Entity Entity::AttachedToEntity
            39	008D	ldnull
            40	008E	call	bool [UnityEngine]UnityEngine.Object::op_Inequality(class [UnityEngine]UnityEngine.Object, class [UnityEngine]UnityEngine.Object)
            41	0093	brfalse.s	48 (00AC) ldarg.0 
            42	0095	ldarg.0
            43	0096	ldfld	class PlayerMoveController PlayerMoveController/WN::ZZ
            44	009B	ldfld	class EntityPlayerLocal PlayerMoveController::entityPlayerLocal
            45	00A0	ldfld	class Entity Entity::AttachedToEntity
            46	00A5	isinst	EntityVehicle
            47	00AA	brtrue.s	62 (00DE) ret 
            // 13 opcodes to remove

         48	00AC	ldarg.0
         49	00AD	ldfld	class PlayerMoveController PlayerMoveController/WN::ZZ
         50	00B2	ldfld	class GUIWindowManager PlayerMoveController::windowManager
         51	00B7	ldstr	"radial"
         52	00BC	ldc.i4.1
         53	00BD	ldc.i4.1
         54	00BE	ldc.i4.1
         55	00BF	callvirt	instance void GUIWindowManager::Open(string, bool, bool, bool)
         56	00C4	ldarg.0
         57	00C5	ldfld	class PlayerMoveController PlayerMoveController/WN::ZZ
         58	00CA	ldfld	class LocalPlayerUI PlayerMoveController::playerUI
         59	00CF	callvirt	instance class XUi LocalPlayerUI::get_xui()
         60	00D4	ldfld	class XUiC_Radial XUi::RadialWindow
         61	00D9	callvirt	instance void XUiC_Radial::SetupMenuData()
         62	00DE	ret

         // PlayerMoveController.WN
         // Token: 0x06005DAC RID: 23980 RVA: 0x002B4DA8 File Offset: 0x002B2FA8
         internal void GZ()
         {
	         this.ZZ.entityPlayerLocal.AimingGun = false;
	         if (this.ZZ.windowManager.IsWindowOpen("windowpaging"))
	         {
		         this.ZZ.windowManager.CloseAllOpenWindows(null);
		         if (this.ZZ.windowManager.IsWindowOpen("windowpaging"))
		         {
			         this.ZZ.windowManager.Close("windowpaging");
		         }
	         }
	         else
	         {
		         this.ZZ.windowManager.CloseAllOpenWindows(null);
		         if (!(this.ZZ.entityPlayerLocal.AttachedToEntity != null) || !(this.ZZ.entityPlayerLocal.AttachedToEntity is EntityVehicle))
		         {
			         this.ZZ.windowManager.Open("radial", true, true, true);
			         this.ZZ.playerUI.xui.RadialWindow.SetupMenuData();
		         }
	         }
         }

      */
      #endregion
   }

   private bool ModifyWindowSelectorMethod(ModuleDefinition module)
   {
      int delNextLines = 0;
      var XUiC_WindowSelectorClass = module.Types.FirstOrDefault(c => c.Name == "XUiC_WindowSelector");
      if (XUiC_WindowSelectorClass == null)
      {
         Logging.LogError("Failed to find the XUiC_WindowSelector class.");
         return false;
      }

      var openSel = XUiC_WindowSelectorClass.Methods.FirstOrDefault(m => m.Name == "OpenSelectorAndWindow");
      if (openSel == null)
      {
         Logging.LogError("Failed to find the OpenSelectorAndWindow method.");
         return false;
      }
      Logging.LogInfo("Found second method to modify: " + openSel);

      var openSelInstructions = openSel.Body.Instructions;
      bool found1 = false, found2 = false;

      foreach (var inst in openSelInstructions.Reverse())
      {
         if (inst.OpCode == OpCodes.Brtrue_S && inst.Previous.OpCode == OpCodes.Isinst && inst.Previous.Operand.ToString() == "EntityVehicle")
         {
            Logging.LogInfo(string.Format("Removing OpCode: {0} Operand: {1}", inst.OpCode, inst.Operand));
            openSelInstructions.Remove(inst);
            delNextLines = 3;
            found1 = true;
            continue;
         }
         else if (inst.OpCode == OpCodes.Brfalse_S && inst.Previous.OpCode == OpCodes.Call && inst.Previous.Operand.ToString().Contains("UnityEngine.Object::op_Inequality")
            && inst.Previous.Previous.OpCode == OpCodes.Ldnull && inst.Previous.Previous.Previous.OpCode == OpCodes.Ldfld
            && inst.Previous.Previous.Previous.Operand.ToString() == "Entity Entity::AttachedToEntity")
         {
            Logging.LogInfo(string.Format("Removing OpCode: {0} Operand: {1}", inst.OpCode, inst.Operand));
            openSelInstructions.Remove(inst);
            delNextLines = 4;
            found2 = true;
            continue;
         }

         if (delNextLines > 0)
         {
            Logging.LogInfo(string.Format("Removing OpCode: {0} Operand: {1}", inst.OpCode, inst.Operand));
            openSelInstructions.Remove(inst);
            delNextLines--;
         }

      }

      if (found1 && found2)
         return true;

      Logging.LogError("Failed to locate both code locations to modify in XUiC_WindowSelector::OpenSelectorAndWindow.");
      return false;

      #region IL Code
      /*
      XUiC_WindowSelector::OpenSelectorAndWindow

      0  0000  ldarg.0
      1  0001  call  class LocalPlayerUI LocalPlayerUI::GetUIForPlayer(class EntityPlayerLocal)
      2	0006	stloc.0
      3	0007	ldarg.0
      4	0008	stloc.1
      5	0009	ldarg.1
      6	000A callvirt instance string[mscorlib] System.String::ToLower()
      7	000F	starg selectedPage(1)

         // if statement to modify
         8	0013	ldloc.1
         9	0014	ldfld class Entity Entity::AttachedToEntity
         10	0019	ldnull
         11	001A call  bool[UnityEngine] UnityEngine.Object::op_Inequality(class [UnityEngine] UnityEngine.Object, class [UnityEngine] UnityEngine.Object)
         12	001F	brfalse.s	17 (002E) ldloc.1 
         13	0021	ldloc.1
         14	0022	ldfld class Entity Entity::AttachedToEntity
         15	0027	isinst EntityVehicle
         16	002C brtrue.s 20 (0036) ret
         // 9 opcodes total

      17	002E	ldloc.1
      18	002F	callvirt instance bool EntityAlive::IsDead()
      19	0034	brfalse.s	21 (0037) ldloc.0 
      20	0036	ret
      21	0037	ldloc.0
      22	0038	callvirt instance class XUi LocalPlayerUI::get_xui()
      23	003D	ldstr	"windowpaging"
      24	0042	callvirt instance class XUiController XUi::FindWindowGroupByName(string)
      25	0047	callvirt instance class XUiController XUiController::GetChildByType<class XUiC_WindowSelector>()
      26	004C castclass   XUiC_WindowSelector
      27	0051	stloc.2
      28	0052	ldloc.0
      29	0053	callvirt instance class NGUIWindowManager LocalPlayerUI::get_nguiWindowManager()
      30	0058	ldc.i4.s	0x45
      31	005A callvirt instance bool NGUIWindowManager::IsShowing(valuetype EnumNGUIWindow)
      32	005F	brfalse.s	37 (006E) ldloc.0 
      33	0061	ldloc.0
      34	0062	callvirt instance class NGUIWindowManager LocalPlayerUI::get_nguiWindowManager()
      35	0067	ldc.i4.s	0x45
      36	0069	callvirt instance void NGUIWindowManager::Close(valuetype EnumNGUIWindow)
      37	006E	ldloc.0
      38	006F	callvirt instance class GUIWindowManager LocalPlayerUI::get_windowManager()
      39	0074	ldstr	"windowpaging"
      40	0079	callvirt instance bool GUIWindowManager::IsWindowOpen(string)
      41	007E	brfalse.s	66 (00CC) ldloc.2 
      42	0080	ldloc.2
      43	0081	callvirt instance string XUiC_WindowSelector::get_SelectedName()
      44	0086	callvirt instance string[mscorlib] System.String::ToLower()
      45	008B ldarg.1
      46	008C call  bool[mscorlib] System.String::op_Equality(string, string)
      47	0091	brfalse.s	66 (00CC) ldloc.2 
      48	0093	ldloc.2
      49	0094	ldfld bool XUiC_WindowSelector::OverrideClose
      50	0099	brtrue.s 66 (00CC) ldloc.2 
      51	009B ldloc.0
      52	009C callvirt instance class GUIWindowManager LocalPlayerUI::get_windowManager()
      53	00A1 ldnull
      54	00A2 callvirt instance bool GUIWindowManager::CloseAllOpenWindows(class GUIWindow)
      55	00A7 pop
      56	00A8 ldloc.0
      57	00A9 callvirt instance class GUIWindowManager LocalPlayerUI::get_windowManager()
      58	00AE ldstr	"windowpaging"
      59	00B3 callvirt instance bool GUIWindowManager::IsWindowOpen(string)
      60	00B8 brfalse.s   98 (012F) ret 
      61	00BA ldloc.0
      62	00BB callvirt instance class GUIWindowManager LocalPlayerUI::get_windowManager()
      63	00C0 ldstr	"windowpaging"
      64	00C5 callvirt instance void GUIWindowManager::Close(string)
      65	00CA br.s  98 (012F) ret 
      66	00CC ldloc.2
      67	00CD ldarg.1
      68	00CE callvirt instance void XUiC_WindowSelector::SetSelected(string)
      69	00D3	ldloc.0
      70	00D4	callvirt instance class GUIWindowManager LocalPlayerUI::get_windowManager()
      71	00D9	ldstr	"windowpaging"
      72	00DE callvirt instance bool GUIWindowManager::IsWindowOpen(string)
      73	00E3	brfalse.s	77 (00ED) ldloc.0 
      74	00E5	ldloc.2
      75	00E6	callvirt instance void XUiC_WindowSelector::OpenSelectedWindow()
      76	00EB br.s  89 (010D) ldloc.0 
      77	00ED	ldloc.0
      78	00EE callvirt instance class GUIWindowManager LocalPlayerUI::get_windowManager()
      79	00F3	ldnull
      80	00F4	callvirt instance bool GUIWindowManager::CloseAllOpenWindows(class GUIWindow)
      81	00F9	pop
      82	00FA ldloc.0
      83	00FB callvirt instance class GUIWindowManager LocalPlayerUI::get_windowManager()
      84	0100	ldstr	"windowpaging"
      85	0105	ldc.i4.0
      86	0106	ldc.i4.0
      87	0107	ldc.i4.1
      88	0108	callvirt instance void GUIWindowManager::Open(string, bool, bool, bool)
      89	010D	ldloc.0
      90	010E	callvirt instance class GUIWindowManager LocalPlayerUI::get_windowManager()
      91	0113	ldstr	"compass"
      92	0118	callvirt instance bool GUIWindowManager::IsWindowOpen(string)
      93	011D	brfalse.s	98 (012F) ret 
      94	011F	ldloc.0
      95	0120	callvirt instance class GUIWindowManager LocalPlayerUI::get_windowManager()
      96	0125	ldstr	"compass"
      97	012A callvirt instance void GUIWindowManager::Close(string)
      98	012F	ret

      // XUiC_WindowSelector
      // Token: 0x06004EB4 RID: 20148 RVA: 0x0023B64C File Offset: 0x0023984C
      public static void OpenSelectorAndWindow(EntityPlayerLocal _localPlayer, string selectedPage)
      {
	      LocalPlayerUI uiforPlayer = LocalPlayerUI.GetUIForPlayer(_localPlayer);
	      selectedPage = selectedPage.ToLower();
	      if ((_localPlayer.AttachedToEntity != null && _localPlayer.AttachedToEntity is EntityVehicle) || _localPlayer.IsDead())
	      {
		      return;
	      }
	      XUiC_WindowSelector xuiC_WindowSelector = (XUiC_WindowSelector)uiforPlayer.xui.FindWindowGroupByName("windowpaging").GetChildByType<XUiC_WindowSelector>();
	      if (uiforPlayer.nguiWindowManager.IsShowing(EnumNGUIWindow.FocusHealthState))
	      {
		      uiforPlayer.nguiWindowManager.Close(EnumNGUIWindow.FocusHealthState);
	      }
	      if (uiforPlayer.windowManager.IsWindowOpen("windowpaging") && xuiC_WindowSelector.SelectedName.ToLower() == selectedPage && !xuiC_WindowSelector.OverrideClose)
	      {
		      uiforPlayer.windowManager.CloseAllOpenWindows(null);
		      if (uiforPlayer.windowManager.IsWindowOpen("windowpaging"))
		      {
			      uiforPlayer.windowManager.Close("windowpaging");
		      }
	      }
	      else
	      {
		      xuiC_WindowSelector.SetSelected(selectedPage);
		      if (uiforPlayer.windowManager.IsWindowOpen("windowpaging"))
		      {
			      xuiC_WindowSelector.OpenSelectedWindow();
		      }
		      else
		      {
			      uiforPlayer.windowManager.CloseAllOpenWindows(null);
			      uiforPlayer.windowManager.Open("windowpaging", false, false, true);
		      }
		      if (uiforPlayer.windowManager.IsWindowOpen("compass"))
		      {
			      uiforPlayer.windowManager.Close("compass");
		      }
	      }
      }
      */
      #endregion
   }

   public bool Link(ModuleDefinition gameModule, ModuleDefinition modModule)
   {
      return true;
   }

   private void SetMethodToVirtual(MethodDefinition meth)
   {
      meth.IsVirtual = true;
   }
   private void SetFieldToPublic(FieldDefinition field)
   {
      field.IsFamily = false;
      field.IsPrivate = false;
      field.IsPublic = true;
   }

   private void SetClassToPublic(TypeDefinition classDef)
   {
      if (classDef == null)
         return;

      classDef.IsPublic = true;
      classDef.IsNotPublic = false;
   }

   private void SetNestedClassToPublic(TypeDefinition classDef)
   {
      if (classDef == null)
         return;
      classDef.IsNestedPublic = true;
   }

   private void SetMethodToPublic(MethodDefinition field)
   {
      field.IsFamily = false;
      field.IsPrivate = false;
      field.IsPublic = true;
   }
}
