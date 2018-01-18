using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using SDX.Compiler;
using SDX.Core;

public class ItemEatDelayRemover : IPatcherMod
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
      if (!ModifyItemActionEat(module))
         throw new Exception("Failed to find and modify the required method!");
   }

   private bool ModifyItemActionEat(ModuleDefinition module)
   {
      var itemActionEat = module.Types.FirstOrDefault(c => c.Name == "ItemActionEat");
      if (itemActionEat != null)
      {
         var method = itemActionEat.Methods.FirstOrDefault(m => m.Name == "OnHoldingUpdate");
         var instructions = method.Body.Instructions;
         if (instructions[6].OpCode == OpCodes.Call && instructions[10].OpCode == OpCodes.Ldsfld && instructions[16].OpCode == OpCodes.Ldelema && instructions[18].OpCode == OpCodes.Blt_Un)
         {
            int delNextLines = 0;
            bool found = false;
            foreach (var inst in instructions.Reverse())
            {
               if (delNextLines > 0)
               {
                  Logging.LogInfo(string.Format("Removing OpCode: {0} Operand: {1}", inst.OpCode, inst.Operand));
                  instructions.Remove(inst);
                  delNextLines--;
                  continue;
               }
               if (inst.OpCode == OpCodes.Blt_Un && inst.Previous.OpCode == OpCodes.Ldfld && inst.Previous.Operand.ToString().Equals("System.Single AnimationDelayData/AnimationDelays::RayCast")
                  && inst.Previous.Previous.OpCode == OpCodes.Ldelema)
               {
                  Logging.LogInfo(string.Format("Found start of code to remove..."));
                  Logging.LogInfo(string.Format("Removing OpCode: {0} Operand: {1}", inst.OpCode, inst.Operand));
                  instructions.Remove(inst);
                  delNextLines = 12;
                  found = true;
               }
            }
            if (found)
               return true;
         }
      }

      return false;

      #region IL code
      /*
         // if (vg.QZ && Time.time - vg.lastUseTime >= AnimationDelayData.AnimationDelay[vg.invData.item.HoldType.Value].RayCast)

         0	0000	ldarg.1
         1	0001	castclass	ItemActionEat/IG
         2	0006	stloc.0
         3	0007	ldloc.0
         4	0008	ldfld	bool ItemActionEat/IG::WZ
         5	000D	brfalse	270 (0381) ret 

            // portion of the if statement to remove.  The part where it checks if the animation has completely played and just before you stop holding the item in question
            6	0012	call	float32 [UnityEngine]UnityEngine.Time::get_time()
            7	0017	ldloc.0
            8	0018	ldfld	float32 ItemActionData::lastUseTime
            9	001D	sub
            10	001E	ldsfld	valuetype AnimationDelayData/AnimationDelays[] AnimationDelayData::AnimationDelay
            11	0023	ldloc.0
            12	0024	ldfld	class ItemInventoryData ItemActionData::invData
            13	0029	ldfld	class ItemClass ItemInventoryData::item
            14	002E	ldfld	class DataItem`1<int32> ItemClass::HoldType
            15	0033	callvirt	instance !0 class DataItem`1<int32>::get_Value()
            16	0038	ldelema	AnimationDelayData/AnimationDelays
            17	003D	ldfld	float32 AnimationDelayData/AnimationDelays::RayCast
            18	0042	blt.un	270 (0381) ret 
            // 13 opcodes to remove in total
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
