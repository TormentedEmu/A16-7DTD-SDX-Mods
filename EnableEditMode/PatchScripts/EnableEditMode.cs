using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using SDX.Compiler;
using SDX.Core;

/// <summary>
/// Patches all the codes to enable the built in edit mode in the game
/// TormentedEmu 2018 tormentedemu@gmail.com
/// </summary>
public class EnableEditMode : IPatcherMod
{
   public bool Patch(ModuleDefinition module)
   {
      Logging.LogInfo("Start patch process..." + System.Reflection.MethodBase.GetCurrentMethod().ReflectedType);
      ModifyAssembly(module);
      Logging.LogInfo("Patch mod complete.");
      return true;
   }

   private void ModifyAssembly(ModuleDefinition module)
   {
      if (!ModifyGameManager(module) ||
          !ModifyWorld(module) ||
          !ModifyGameMode(module) ||
          !ModifyXUiC_CreateWorld(module) ||
          !ModifyChunkProvider(module))
         throw new Exception("Failed to find and modify all of the required methods!");
   }

   private bool ModifyGameManager(ModuleDefinition module)
   {
      // check if the class to modify exists
      TypeDefinition gameManager = module.Types.FirstOrDefault(c => c.Name == "GameManager");
      if (gameManager == null)
      {
         Logging.LogError("Failed to find class GameManager.");
         return false;
      }

      MethodDefinition gmStartAsServer = null; // 16.4(b8) named as private IEnumerator OQ() - but we need to edit the hidden opcodes

      foreach (TypeDefinition td in gameManager.NestedTypes)
      {
         //Logging.LogInfo(string.Format("NestedTypes: {0}, FullName: {1}", td.Name, td.FullName));
         if (td.HasInterfaces)
         {
            foreach (var inf in td.Interfaces)
            {
               //Logging.LogInfo(string.Format("  Interface: {0}, {1}", inf.Name, inf.FullName));
            }
         }

         MethodDefinition moveNext = td.Methods.FirstOrDefault(m => m.Name == "MoveNext");
         if (moveNext != null)
         {
            foreach (Instruction inst in moveNext.Body.Instructions)
            {
               //Logging.LogInfo(string.Format("     Inst OpCode: {0} Operand: {1}", inst.OpCode, inst.Operand));
               if (inst.OpCode == OpCodes.Ldstr && inst.Operand.ToString() == "StartAsServer" &&
                   inst.Next.OpCode == OpCodes.Call && inst.Next.Operand.ToString() == "System.Void Log::Out(System.String)")
               {
                  Logging.LogInfo(string.Format("Found gmStartAsServer method to modify: type: {0} name: {1}", td, moveNext.Name));
                  gmStartAsServer = moveNext;
                  break;
               }
            }
         }

         if (gmStartAsServer != null)
            break;
      }

      if (gmStartAsServer == null)
      {
         Logging.LogError("Failed to find gmStartAsServer method to modify.");
         return false;
      }

      int delNextLines = 0;

      bool found1 = false;
      bool found2 = false;
      int i = gmStartAsServer.Body.Instructions.Count;
      MethodDefinition gmSavePersistentPlayers = null; // 16.4(b8) named as private void SG() - referenced within this method gmStartAsServer
      foreach (Instruction inst in gmStartAsServer.Body.Instructions.Reverse())
      {
         i--;

         if (inst.OpCode == OpCodes.Callvirt && inst.Operand.ToString() == "System.Boolean PersistentPlayerList::CleanupPlayers()" &&
             inst.Next.OpCode == OpCodes.Brfalse_S &&
             inst.Next.Next.OpCode == OpCodes.Ldarg_0 &&
             inst.Next.Next.Next.OpCode == OpCodes.Ldfld && inst.Next.Next.Next.Operand.ToString().Contains("GameManager GameManager/") &&
             inst.Next.Next.Next.Next.OpCode == OpCodes.Call && inst.Next.Next.Next.Next.Operand.ToString().Contains("System.Void GameManager::"))
         {
            gmSavePersistentPlayers = ((MethodReference)inst.Next.Next.Next.Next.Operand).Resolve();
            Logging.LogInfo(string.Format("Found gmSavePersistentPlayers method to modify: name: {0}", gmSavePersistentPlayers.Name));
         }

         if (delNextLines > 0)
         {
            Logging.LogInfo(string.Format("Removing OpCode: {0} Operand: {1}", inst.OpCode, inst.Operand));
            gmStartAsServer.Body.Instructions.Remove(inst);
            delNextLines--;
            continue;
         }

         if (inst.OpCode == OpCodes.Stfld && inst.Operand.ToString() == "System.Int32 PlayerDataFile::id" &&
             inst.Previous.OpCode == OpCodes.Ldc_I4_M1 &&
             inst.Previous.Previous.OpCode == OpCodes.Ldfld && inst.Previous.Previous.Operand.ToString().Contains("PlayerDataFile GameManager/") &&
             inst.Previous.Previous.Previous.OpCode == OpCodes.Ldarg_0 &&
             inst.Previous.Previous.Previous.Previous.OpCode == OpCodes.Brfalse_S &&
             inst.Previous.Previous.Previous.Previous.Previous.OpCode == OpCodes.Ldfld && inst.Previous.Previous.Previous.Previous.Previous.Operand.ToString().Contains("System.Boolean GameManager::") &&
             inst.Previous.Previous.Previous.Previous.Previous.Previous.OpCode == OpCodes.Ldfld && inst.Previous.Previous.Previous.Previous.Previous.Previous.Operand.ToString().Contains("GameManager GameManager/") &&
             inst.Previous.Previous.Previous.Previous.Previous.Previous.Previous.OpCode == OpCodes.Ldarg_0)
         {
            Logging.LogInfo(string.Format("Found our first lot of opcodes to remove at opcode index: {0}", i));
            Logging.LogInfo(string.Format("Removing OpCode: {0} Operand: {1}", inst.OpCode, inst.Operand));
            gmStartAsServer.Body.Instructions.Remove(inst);
            delNextLines = 7;
            found1 = true;
            continue;
         }
         if (inst.OpCode == OpCodes.Br_S &&
             inst.Previous.OpCode == OpCodes.Stfld && inst.Previous.Operand.ToString().Contains("PersistentPlayerList GameManager::persistentPlayer") &&
             inst.Previous.Previous.OpCode == OpCodes.Newobj && inst.Previous.Previous.Operand.ToString() == "System.Void PersistentPlayerList::.ctor()" &&
             inst.Previous.Previous.Previous.OpCode == OpCodes.Ldfld && inst.Previous.Previous.Previous.Operand.ToString().Contains("GameManager GameManager/"))
         {
            Logging.LogInfo(string.Format("Found our second lot of opcodes to remove at opcode index: {0}", i));
            Logging.LogInfo(string.Format("Removing OpCode: {0} Operand: {1}", inst.OpCode, inst.Operand));
            gmStartAsServer.Body.Instructions.Remove(inst);
            delNextLines = 8;
            found2 = true;
            continue;
         }
      }

      if (gmSavePersistentPlayers == null)
      {
         Logging.LogError("Failed to find our gmSavePersistentPlayers method.");
         return false;
      }

      bool foundgmSavePersistentPlayers = false;
      if (gmSavePersistentPlayers != null)
      {
         i = gmSavePersistentPlayers.Body.Instructions.Count;
         delNextLines = 0;
         foreach (Instruction inst in gmSavePersistentPlayers.Body.Instructions.Reverse())
         {
            i--;

            if (delNextLines > 0)
            {
               Logging.LogInfo(string.Format("Removing OpCode: {0} Operand: {1}", inst.OpCode, inst.Operand));
               gmSavePersistentPlayers.Body.Instructions.Remove(inst);
               delNextLines--;
               continue;
            }

            if (inst.OpCode == OpCodes.Ldarg_0 &&
                inst.Next.OpCode == OpCodes.Ldfld && inst.Next.Operand.ToString() == "PersistentPlayerList GameManager::persistentPlayers" &&
                inst.Next.Next.OpCode == OpCodes.Brfalse_S &&
                inst.Previous.OpCode == OpCodes.Brtrue_S &&
                inst.Previous.Previous.OpCode == OpCodes.Ldfld && inst.Previous.Previous.Operand.ToString().Contains("System.Boolean GameManager::") &&
                inst.Previous.Previous.Previous.OpCode == OpCodes.Ldarg_0)
            {
               Logging.LogInfo(string.Format("Found our opcodes to remove at opcode index: {0}", i));
               delNextLines = 3;
               foundgmSavePersistentPlayers = true;
               continue;
            }
         }
      }

      bool foundgmUpdateSub = false;
      MethodDefinition gmUpdateSub = null; // JQ()
      MethodDefinition gmUpdate = gameManager.Methods.First(m => m.Name == "Update");
      foreach (Instruction inst in gmUpdate.Body.Instructions)
      {
         if (inst.OpCode == OpCodes.Call)
         {
            gmUpdateSub = ((MethodReference)inst.Operand).Resolve();
            break;
         }
      }

      if (gmUpdateSub != null)
      {
         Logging.LogInfo(string.Format("Found gmUpdateSub method to modify: {0}", gmUpdateSub.Name));

         i = gmUpdateSub.Body.Instructions.Count;
         delNextLines = 0;
         foreach (Instruction inst in gmUpdateSub.Body.Instructions.Reverse())
         {
            i--;

            if (delNextLines > 0)
            {
               Logging.LogInfo(string.Format("Removing OpCode: {0} Operand: {1}", inst.OpCode, inst.Operand));
               gmUpdateSub.Body.Instructions.Remove(inst);
               delNextLines--;
               continue;
            }

            if (inst.OpCode == OpCodes.Ldarg_0 &&
                inst.Next.OpCode == OpCodes.Ldfld && inst.Next.Operand.ToString().Contains("World GameManager::") &&
                inst.Next.Next.OpCode == OpCodes.Callvirt && inst.Next.Next.Operand.ToString() == "System.Void World::SaveWorldState()" &&
                inst.Previous.OpCode == OpCodes.Brtrue_S &&
                inst.Previous.Previous.OpCode == OpCodes.Ldfld && inst.Previous.Previous.Operand.ToString().Contains("System.Boolean GameManager::") &&
                inst.Previous.Previous.Previous.OpCode == OpCodes.Ldarg_0)
            {
               Logging.LogInfo(string.Format("Found our first lot of opcodes to remove at opcode index: {0}", i));
               delNextLines = 3;
               foundgmUpdateSub = true;
               continue;
            }

         }
      }

      bool foundgmSaveAndCleanupWorld_1 = false;
      bool foundgmSaveAndCleanupWorld_2 = false;
      MethodDefinition gmSaveAndCleanupWorld = gameManager.Methods.FirstOrDefault(m => m.Name == "SaveAndCleanupWorld");

      if (gmSaveAndCleanupWorld != null)
      {
         Logging.LogInfo(string.Format("Found gmSaveAndCleanupWorld method to modify: {0}", gmSaveAndCleanupWorld.Name));

         i = gmSaveAndCleanupWorld.Body.Instructions.Count;
         delNextLines = 0;
         foreach (Instruction inst in gmSaveAndCleanupWorld.Body.Instructions.Reverse())
         {
            i--;

            if (delNextLines > 0)
            {
               Logging.LogInfo(string.Format("Removing OpCode: {0} Operand: {1}", inst.OpCode, inst.Operand));
               gmSaveAndCleanupWorld.Body.Instructions.Remove(inst);
               delNextLines--;
               continue;
            }

            if (inst.OpCode == OpCodes.Brtrue_S &&
                inst.Previous.OpCode == OpCodes.Call && inst.Previous.Operand.ToString() == "System.Boolean GameManager::IsEditMode()" &&
                inst.Previous.Previous.OpCode == OpCodes.Ldarg_0 &&
                inst.Next.OpCode == OpCodes.Call && inst.Next.Operand.ToString() == "VehicleManager VehicleManager::get_Instance()")
            {
               Logging.LogInfo(string.Format("Found our first lot of opcodes to remove at opcode index: {0}", i));
               gmSaveAndCleanupWorld.Body.Instructions.Remove(inst);
               delNextLines = 2;
               foundgmSaveAndCleanupWorld_1 = true;
               continue;
            }

            if (inst.OpCode == OpCodes.Brtrue &&
                inst.Previous.OpCode == OpCodes.Call && inst.Previous.Operand.ToString() == "System.Boolean GameManager::IsEditMode()" &&
                inst.Previous.Previous.OpCode == OpCodes.Ldarg_0 &&
                inst.Next.OpCode == OpCodes.Ldarg_0 &&
                inst.Next.Next.OpCode == OpCodes.Ldfld && inst.Next.Next.Operand.ToString().Contains("World GameManager::"))
            {
               Logging.LogInfo(string.Format("Found our second lot of opcodes to remove at opcode index: {0}", i));
               gmSaveAndCleanupWorld.Body.Instructions.Remove(inst);
               delNextLines = 2;
               foundgmSaveAndCleanupWorld_2 = true;
               continue;
            }
         }
      }

      if (found1 && found2 && foundgmSavePersistentPlayers && foundgmUpdateSub && foundgmSaveAndCleanupWorld_1 && foundgmSaveAndCleanupWorld_2)
      {
         Logging.LogInfo("Found all opcodes to modify within GameManager.");
         return true;
      }

      Logging.LogError("Failed to find all opcodes to modify within GameManager.");
      return false;
   }

   private bool ModifyWorld(ModuleDefinition module)
   {
      Logging.LogInfo("ModifyWorld start.");

      TypeDefinition world = module.Types.FirstOrDefault(c => c.Name == "World");
      if (world == null)
      {
         Logging.LogError("Failed to find the class World.");
         return false;
      }

      MethodDefinition loadWorld = world.Methods.FirstOrDefault(m => m.Name == "LoadWorld");
      if (loadWorld == null)
      {
         Logging.LogError("Failed to find the method LoadWorld.");
         return false;
      }

      MethodDefinition save = world.Methods.FirstOrDefault(m => m.Name == "Save");
      if (save == null)
      {
         Logging.LogError("Failed to find the method Save.");
         return false;
      }

      int delNextLInes = 0;
      bool foundLoadWorldOps_1 = false, foundLoadWorldOps_2 = false;
      int idx = loadWorld.Body.Instructions.Count;
      foreach (Instruction inst in loadWorld.Body.Instructions.Reverse())
      {
         idx--;

         if (delNextLInes > 0)
         {
            Logging.LogInfo(string.Format("Removing OpCode: {0} Operand: {1}", inst.OpCode, inst.Operand));
            loadWorld.Body.Instructions.Remove(inst);
            delNextLInes--;
            continue;
         }

         if (inst.OpCode == OpCodes.Br &&
             inst.Next.OpCode == OpCodes.Call && inst.Next.Operand.ToString() == "System.String GameUtils::GetSaveGameDir()" &&
             inst.Previous.OpCode == OpCodes.Stloc_0 &&
             inst.Previous.Previous.OpCode == OpCodes.Call && inst.Previous.Previous.Operand.ToString() == "System.String System.String::Concat(System.String,System.String,System.String,System.String)" &&
             inst.Previous.Previous.Previous.Previous.Previous.Previous.Previous.OpCode == OpCodes.Ldstr && inst.Previous.Previous.Previous.Previous.Previous.Previous.Previous.Operand.ToString() == "Data/Worlds")
         {
            Logging.LogInfo(string.Format("Found first opcodes to remove at index: {0}", idx));
            Logging.LogInfo(string.Format("Removing OpCode: {0} Operand: {1}", inst.OpCode, inst.Operand));
            foundLoadWorldOps_1 = true;
            delNextLInes = 10;
            loadWorld.Body.Instructions.Remove(inst);
            continue;
         }

         if (inst.OpCode == OpCodes.Brtrue_S &&
             inst.Previous.OpCode == OpCodes.Ldfld && inst.Previous.Operand.ToString() == "System.Boolean World::bEditorMode" &&
             inst.Previous.Previous.OpCode == OpCodes.Ldarg_0)
         {
            Logging.LogInfo(string.Format("Found second opcodes to remove at index: {0}", idx));
            Logging.LogInfo(string.Format("Removing OpCode: {0} Operand: {1}", inst.OpCode, inst.Operand));
            foundLoadWorldOps_2 = true;
            delNextLInes = 2;
            loadWorld.Body.Instructions.Remove(inst);
            continue;
         }
      }

      delNextLInes = 0;
      bool foundSaveOps = false;
      idx = save.Body.Instructions.Count;
      foreach (Instruction inst in save.Body.Instructions.Reverse())
      {
         idx--;

         if (delNextLInes > 0)
         {
            Logging.LogInfo(string.Format("Removing OpCode: ({0}) {1} Operand: {2}", idx, inst.OpCode, inst.Operand));
            save.Body.Instructions.Remove(inst);
            delNextLInes--;
            continue;
         }

         if (inst.OpCode == OpCodes.Br_S &&
             inst.Next.OpCode == OpCodes.Ldarg_0 &&
             inst.Next.Next.OpCode == OpCodes.Ldfld && inst.Next.Next.Operand.ToString().Contains("WorldState World::") &&
             inst.Next.Next.Next.OpCode == OpCodes.Call && inst.Next.Next.Next.Operand.ToString() == "System.String GameUtils::GetSaveGameDir()" &&
             inst.Previous.OpCode == OpCodes.Pop &&
             inst.Previous.Previous.OpCode == OpCodes.Callvirt && inst.Previous.Previous.Operand.ToString() == "System.Boolean WorldState::Save(System.String)" &&
             inst.Previous.Previous.Previous.Previous.Previous.Previous.Previous.Previous.OpCode == OpCodes.Call &&
             inst.Previous.Previous.Previous.Previous.Previous.Previous.Previous.Previous.Operand.ToString() == "System.String Utils::GetGameDir(System.String)" &&
             save.Body.Instructions[idx - 20].OpCode == OpCodes.Ldfld && save.Body.Instructions[idx - 20].Operand.ToString() == "System.Boolean World::bEditorMode")
         {
            Logging.LogInfo(string.Format("Found first opcodes to remove at index: {0}", idx));
            Logging.LogInfo(string.Format("Removing OpCode: ({0}) {1} Operand: {2}", idx, inst.OpCode, inst.Operand));
            foundSaveOps = true;
            delNextLInes = 21;
            save.Body.Instructions.Remove(inst);
            continue;
         }
      }

      if (foundLoadWorldOps_1 && foundLoadWorldOps_2 && foundSaveOps)
         return true;

      Logging.LogError("Failed to modify all opcodes in the method LoadWorld or Save.");
      return false;
   }

   private bool ModifyGameMode(ModuleDefinition module)
   {
      TypeDefinition gameMode = module.Types.FirstOrDefault(c => c.Name == "GameMode");
      if (gameMode == null)
      {
         Logging.LogError("Failed to find the class GameMode.");
         return false;
      }

      foreach (MethodDefinition md in gameMode.Methods)
      {
      Logging.LogInfo(string.Format("Method: {0}, FullName: {1}", md.Name, md.FullName));
      }

      MethodDefinition staticConstructor = gameMode.Methods.FirstOrDefault(m => m.Name == ".cctor");
      if (staticConstructor == null)
      {
         Logging.LogError("Failed to find the method staticConstructor.");
         return false;
      }

      TypeDefinition gmEditWorld = module.Types.FirstOrDefault(c => c.Name == "GameModeEditWorld");
      if (gmEditWorld == null)
      {
         Logging.LogError("Failed to find the class GameModeEditWorld.");
         return false;
      }

      MethodDefinition gmEWctor = gmEditWorld.Methods.FirstOrDefault(m => m.Name == ".ctor");
      if (gmEWctor == null)
      {
         Logging.LogError("Failed to find the method GameModeEditWorld::.ctor().");
         return false;
      }

      bool modifiedCode = false;
      var instx = staticConstructor.Body.Instructions;
      if (instx[0].OpCode == OpCodes.Ldc_I4_3 && instx[1].OpCode == OpCodes.Newarr)
      {
         instx[0].OpCode = OpCodes.Ldc_I4_4;
         if (instx[14].OpCode == OpCodes.Stsfld && instx[14].Operand.ToString() == "GameMode[] GameMode::AvailGameModes")
         {
            Instruction gmCollection = instx[14];
            ILProcessor il = staticConstructor.Body.GetILProcessor();
            il.InsertBefore(gmCollection, Instruction.Create(OpCodes.Dup));
            il.InsertBefore(gmCollection, Instruction.Create(OpCodes.Ldc_I4_3));
            il.InsertBefore(gmCollection, Instruction.Create(OpCodes.Newobj, gmEWctor));
            il.InsertBefore(gmCollection, Instruction.Create(OpCodes.Stelem_Ref));

            modifiedCode = true;
         }
         else
         {
            Logging.LogError(string.Format("Failed to modify code:  Instruction 13 is {0}--{1}", instx[13].OpCode, instx[13].Operand));
         }
      }

      if (modifiedCode)
         return true;

      return false;
   }

   private bool ModifyXUiC_CreateWorld(ModuleDefinition module)
   {
      TypeDefinition xuicCreateWorld = module.Types.FirstOrDefault(c => c.Name == "XUiC_CreateWorld");
      if (xuicCreateWorld == null)
      {
         Logging.LogError("Failed to find class XUiC_CreateWorld.");
         return false;
      }

      MethodDefinition onOpen = xuicCreateWorld.Methods.FirstOrDefault(m => m.Name == "OnOpen");
      if (onOpen == null)
      {
         Logging.LogError("Failed to find method XUiC_CreateWorld::OnOpen.");
         return false;
      }

      int idx = onOpen.Body.Instructions.Count;
      int delNextLines = 0;
      bool removedCodes = false;
      foreach (Instruction inst in onOpen.Body.Instructions.Reverse())
      {
         idx--;

         if (inst.OpCode == OpCodes.Call && inst.Operand.ToString() == "System.Void XUiController::OnOpen()" &&
             inst.Previous.OpCode == OpCodes.Ldarg_0)
         {
            break;
         }

         if (delNextLines > 0)
         {
            onOpen.Body.Instructions.Remove(inst);
            delNextLines--;
            continue;
         }

         if (inst.OpCode == OpCodes.Ret)
         {
            delNextLines = 15;
            removedCodes = true;
         }
      }

      if (removedCodes && delNextLines == 0)
         return true;

      return false;
   }

   private bool ModifyChunkProvider(ModuleDefinition module)
   {
      TypeDefinition chunkProviderRandom2 = module.Types.FirstOrDefault(c => c.Name == "ChunkProviderGenerateWorldRandom2");
      if (chunkProviderRandom2 == null)
      {
         Logging.LogError("Failed to find class ChunkProviderGenerateWorldRandom2.");
         return false;
      }

      MethodDefinition init = chunkProviderRandom2.Methods.FirstOrDefault(m => m.Name == "Init");
      if (init == null)
      {
         Logging.LogError("Failed to find method ChunkProviderGenerateWorldRandom2::Init.");
         return false;
      }

      int idx = init.Body.Instructions.Count;
      int delNextLines = 0, skipLines = 0;
      bool removedCodes1 = false, removedCodes2 = false;
      foreach (Instruction inst in init.Body.Instructions.Reverse())
      {
         idx--;

         if (skipLines > 0)
         {
            Logging.LogInfo(string.Format("Skipping OpCode: {0} Operand: {1}", inst.OpCode, inst.Operand));
            skipLines--;
            continue;
         }

         if (delNextLines > 0)
         {
            Logging.LogInfo(string.Format("Removing OpCode: {0} Operand: {1}", inst.OpCode, inst.Operand));
            init.Body.Instructions.Remove(inst);
            delNextLines--;
            continue;
         }

         if (inst.OpCode == OpCodes.Br_S &&
             inst.Next.OpCode == OpCodes.Call && inst.Next.Operand.ToString() == "System.String GameUtils::GetSaveGameRegionDir()" &&
             inst.Previous.OpCode == OpCodes.Ldnull &&
             inst.Previous.Previous.OpCode == OpCodes.Brfalse_S &&
             inst.Previous.Previous.Previous.OpCode == OpCodes.Ldfld && inst.Previous.Previous.Previous.Operand.ToString() == "System.Boolean World::bEditorMode" &&
             inst.Previous.Previous.Previous.Previous.OpCode == OpCodes.Ldarg_1)
         {
            Logging.LogInfo(string.Format("Found first opcodes to remove at index: {0}", idx));
            Logging.LogInfo(string.Format("Removing OpCode: {0} Operand: {1}", inst.OpCode, inst.Operand));
            removedCodes1 = true;
            delNextLines = 4;
            init.Body.Instructions.Remove(inst);
            continue;
         }

         if (inst.OpCode == OpCodes.Ceq &&
             inst.Previous.OpCode == OpCodes.Ldc_I4_0 &&
             inst.Previous.Previous.OpCode == OpCodes.Ldfld && inst.Previous.Previous.Operand.ToString() == "System.Boolean World::bEditorMode" &&
             inst.Previous.Previous.Previous.OpCode == OpCodes.Ldarg_1)
         {
            Logging.LogInfo(string.Format("Found second opcodes to remove at index: {0}", idx));
            Logging.LogInfo(string.Format("Removing OpCode: {0} Operand: {1}", inst.OpCode, inst.Operand));
            removedCodes2 = true;
            delNextLines = 2;
            skipLines = 1;
            inst.Previous.OpCode = OpCodes.Ldc_I4_1;
            init.Body.Instructions.Remove(inst);
         }

      }

      if (removedCodes1 && removedCodes2)
         return true;

      return false;
   }


   public bool Link(ModuleDefinition gameModule, ModuleDefinition modModule)
   {
      return true;
   }

   #region Utility
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
   #endregion
}
