using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using SDX.Compiler;
using SDX.Core;

public class MinibikeImpact : IPatcherMod
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
      if (!ModifyEntityVehicle(module))
            throw new Exception("Failed to find and modify the required method!");
   }

   private bool ModifyEntityVehicle(ModuleDefinition module)
   {
      var eVehicle = module.Types.FirstOrDefault(c => c.Name == "EntityVehicle");
      if (eVehicle != null)
      {
         var method = eVehicle.Methods.FirstOrDefault(m => m.Name == "FixedUpdate");
         var instruction = method.Body.Instructions.FirstOrDefault(i => i.OpCode == OpCodes.Call && i.Operand.ToString().Contains("UnityEngine.Physics::CapsuleCast"));
         if (instruction != null && instruction.Previous.OpCode == OpCodes.Ldc_I4 && instruction.Previous.Operand.Equals(0x8000))
         {
            Logging.LogInfo(string.Format("Found value to modify:  {0}", instruction.Previous.Operand));
            instruction.Previous.Operand = 15;
            return true;
         }
      }

      return false;
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
