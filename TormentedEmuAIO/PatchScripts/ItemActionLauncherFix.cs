using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using SDX.Compiler;
using SDX.Core;

public class ItemActionLauncherFix : IPatcherMod
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
      if (!ModifyItemActionLauncher(module))
         throw new Exception("Failed to find and modify the required method!");
   }

   private bool ModifyItemActionLauncher(ModuleDefinition module)
   {
      // check if the class to modify exists
      var itemActionLauncher = module.Types.FirstOrDefault(c => c.Name == "ItemActionLauncher");
      if (itemActionLauncher == null)
      {
         Logging.LogError("Failed to find class ItemActionLauncher.");
         return false;
      }
      // check to make sure the method doesn't already exist
      var sh = itemActionLauncher.Methods.FirstOrDefault(m => m.Name == "StopHolding");
      if (sh != null)
      {
         Logging.LogError("ItemActionLauncher::StopHolding method found.  Aborting patch...");
         return false;
      }

      // load the various types and methods we need to build our opcodes with.  no safety checks because we're confident ;)
      var itemActionData = module.Types.FirstOrDefault(c => c.Name == "ItemActionData");
      var iadLauncher = itemActionLauncher.NestedTypes.FirstOrDefault(t => t.Name == "ItemActionDataLauncher");
      var iadlProjetcileInst = iadLauncher.Fields.FirstOrDefault(n => n.Name == "projectileInstance");
      var opInequality = module.Import(module.GetTypeReferences().First(t => t.FullName == "UnityEngine.Object").Resolve().Methods.First(m => m.FullName == "System.Boolean UnityEngine.Object::op_Inequality(UnityEngine.Object,UnityEngine.Object)"));
      var getGameObject = module.Import(module.GetTypeReferences().First(t => t.FullName == "UnityEngine.Component").Resolve().Methods.First(m => m.FullName == "UnityEngine.GameObject UnityEngine.Component::get_gameObject()"));
      var destroy = module.Import(module.GetTypeReferences().First(t => t.FullName == "UnityEngine.Object").Resolve().Methods.First(m => m.FullName == "System.Void UnityEngine.Object::Destroy(UnityEngine.Object)"));
      var baseSH = module.Types.First(c => c.Name == "ItemActionRanged").Methods.First(m => m.Name == "StopHolding");

      // define a brand new method to insert into the ItemActionLauncher class
      MethodDefinition method = new MethodDefinition("StopHolding",
         MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
         module.Import(typeof(void)))
         { HasThis = true };

      // define and add the param we need for this new method
      ParameterDefinition p = new ParameterDefinition(itemActionData) { Name = "_itemActionData" };
      method.Parameters.Add(p);

      // create the method body and add it to the class method list
      /*
         public override void StopHolding(ItemActionData _itemActionData)
         {
	         base.StopHolding(_itemActionData);
	         if (((ItemActionLauncher.ItemActionDataLauncher)_itemActionData).projectileInstance != null)
	         {
		         UnityEngine.Object.Destroy(((ItemActionLauncher.ItemActionDataLauncher)_itemActionData).projectileInstance.gameObject);
		         ((ItemActionLauncher.ItemActionDataLauncher)_itemActionData).projectileInstance = null;
	         }
         }
      */

      var ret = Instruction.Create(OpCodes.Ret);
      method.Body.Instructions.Add(ret);
      var il = method.Body.GetILProcessor();
      il.InsertBefore(ret, il.Create(OpCodes.Ldarg_0));
      il.InsertBefore(ret, il.Create(OpCodes.Ldarg_1));
      il.InsertBefore(ret, il.Create(OpCodes.Call, baseSH));
      il.InsertBefore(ret, il.Create(OpCodes.Ldarg_1));
      il.InsertBefore(ret, il.Create(OpCodes.Castclass, iadLauncher));
      il.InsertBefore(ret, il.Create(OpCodes.Ldfld, iadlProjetcileInst));
      il.InsertBefore(ret, il.Create(OpCodes.Ldnull));
      il.InsertBefore(ret, il.Create(OpCodes.Call, opInequality));
      il.InsertBefore(ret, il.Create(OpCodes.Brfalse_S, ret));
      il.InsertBefore(ret, il.Create(OpCodes.Ldarg_1));
      il.InsertBefore(ret, il.Create(OpCodes.Castclass, iadLauncher));
      il.InsertBefore(ret, il.Create(OpCodes.Ldfld, iadlProjetcileInst));
      il.InsertBefore(ret, il.Create(OpCodes.Callvirt, getGameObject));
      il.InsertBefore(ret, il.Create(OpCodes.Call, destroy));
      il.InsertBefore(ret, il.Create(OpCodes.Ldarg_1));
      il.InsertBefore(ret, il.Create(OpCodes.Castclass, iadLauncher));
      il.InsertBefore(ret, il.Create(OpCodes.Ldnull));
      il.InsertBefore(ret, il.Create(OpCodes.Stfld, iadlProjetcileInst));
      itemActionLauncher.Methods.Add(method);
      return true;
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
