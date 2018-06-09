using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using SDX.Compiler;
using SDX.Core;

/// <summary>
/// TormentedEmu 2018 tormentedemu@gmail.com
/// Patches some fields and class methods to override the default behaviour for Recipe management
/// </summary>
public class EmuWorkstationRecipePatch : IPatcherMod
{
  // Notes:
  // every recipe has a craft_area variable
  // xuiC_RecipeEntry.IsCurrentWorkstation
  // XUi public string currentWorkstation
  // Recipe public string craftingArea;
  // XUiC_WindowSelector this.OUW.Text = ((this.RQZ == null) ? string.Empty : Localization.Get(string.Format("xui{0}", this.Selected.ID), string.Empty).ToUpper());


  public bool Patch(ModuleDefinition module)
  {
    Logging.LogInfo("Start patch process..." + System.Reflection.MethodBase.GetCurrentMethod().ReflectedType);
    ModifyAssembly(module);
    Logging.LogInfo("Patch mod complete.");
    return true;
  }

  private void ModifyAssembly(ModuleDefinition module)
  {
    if (!ModifyCraftingManager(module))
      throw new Exception("Failed to find and modify the required method!");
  }

  private bool ModifyCraftingManager(ModuleDefinition module)
  {
    // check if the class to modify exists
    var cm = module.Types.FirstOrDefault(c => c.Name == "CraftingManager");
    if (cm == null)
    {
      Logging.LogError("Failed to find class CraftingManager.");
      return false;
    }

    foreach (var f in cm.Fields)
    {
      f.IsPublic = true;
      f.IsPrivate = false;
    }

    var car = cm.Methods.FirstOrDefault(m => m.Name == "ClearAllRecipes");
    if (car == null)
    {
      Logging.LogError("CraftingManager::ClearAllRecipes method not found.  Aborting patch...");
      return false;
    }

    FieldDefinition masterRecipeList = null;

    foreach (var inst in car.Body.Instructions)
    {
      if (inst.OpCode == OpCodes.Ldsfld && inst.Operand.ToString().Contains("List`1<Recipe> CraftingManager::"))
      {
        masterRecipeList = inst.Operand as FieldDefinition;
        break;
      }
    }

    if (masterRecipeList == null)
    {
      Logging.LogError("Failed to find the static field masterRecipeList.");
      return false;
    }

    masterRecipeList.Name = "MasterRecipeList";

    var glrc = cm.Methods.FirstOrDefault(m => m.Name == "GetLockedRecipeCount");
    if (glrc == null)
    {
      Logging.LogError("CraftingManager::GetLockedRecipeCount method not found.  Aborting patch...");
      return false;
    }

    FieldDefinition masterLockedRecipeList = null;

    foreach (var inst in glrc.Body.Instructions)
    {
      if (inst.OpCode == OpCodes.Ldsfld && inst.Operand.ToString().Contains("List`1<System.String> CraftingManager::"))
      {
        masterLockedRecipeList = inst.Operand as FieldDefinition;
        break;
      }
    }

    if (masterLockedRecipeList == null)
    {
      Logging.LogError("Failed to find the static field masterLockedRecipeList.");
      return false;
    }

    masterLockedRecipeList.Name = "MasterLockedRecipeList";

    return true;
  }

  public bool Link(ModuleDefinition gameModule, ModuleDefinition modModule)
  {
    if (!PatchAssembly(gameModule, modModule))
      throw new Exception("Failed to patch the assembly!");

    return true;
  }

  private bool PatchAssembly(ModuleDefinition gameModule, ModuleDefinition modModule)
  {
    var xuimRec = gameModule.Types.FirstOrDefault(c => c.Name == "XUiM_Recipes");
    if (xuimRec == null)
    {
      Logging.LogError("Failed to find class XUiM_Recipes.");
      return false;
    }

    MethodDefinition getRecipes = xuimRec.Methods.First(m => m.Name == "GetRecipes");
    if (getRecipes == null)
    {
      Logging.LogError("Failed to find method XUiM_Recipes::GetRecipes.");
      return false;
    }

    var emuGetRecipes = gameModule.Import(modModule.Types.First(c => c.Name == "EmuRecipeManager").Resolve().Methods.First(m => m.Name == "GetRecipes"));

    getRecipes.Body.Instructions.Clear();

    var il = getRecipes.Body.GetILProcessor();

    il.Emit(OpCodes.Callvirt, emuGetRecipes);
    il.Emit(OpCodes.Stloc_0);
    il.Emit(OpCodes.Ldloc_0);
    il.Emit(OpCodes.Ret);

    var xuicRecList = gameModule.Types.FirstOrDefault(c => c.Name == "XUiC_RecipeList");
    if (xuicRecList == null)
    {
      Logging.LogError("Failed to find class XUiC_RecipeList.");
      return false;
    }

    var getWS = xuicRecList.Methods.First(m => m.Name == "get_Workstation");

    MethodDefinition refRecipes = xuicRecList.Methods.First(m => m.Name == "RefreshRecipes");
    if (refRecipes == null)
    {
      Logging.LogError("Failed to find method XUiC_RecipeList::RefreshRecipes.");
      return false;
    }

    MethodDefinition wtg = null;

    foreach (var inst in refRecipes.Body.Instructions)
    {
      if (inst.OpCode == OpCodes.Call && inst.Operand.ToString().Contains("XUiC_RecipeList"))
      {
        wtg = inst.Operand as MethodDefinition;
        break;
      }
    }

    if (wtg == null)
    {
      Logging.LogError("Failed to find method XUiC_RecipeList::RefreshRecipes sub method.");
      return false;
    }

    var emuGetRecipes2 = gameModule.Import(modModule.Types.First(c => c.Name == "EmuRecipeManager").Resolve().Methods.First(m => m.Name == "GetRecipesStation"));
    Instruction insertionPoint = null;

    foreach (var inst in wtg.Body.Instructions)
    {
      if (inst.OpCode == OpCodes.Call && inst.Operand.ToString().Contains("XUiM_Recipes::GetRecipes()") && inst.Previous != null && inst.Previous.OpCode == OpCodes.Ldarg_0)
      {
        insertionPoint = inst;
        inst.Operand = emuGetRecipes2;
        break;
      }
    }

    if (insertionPoint == null)
    {
      Logging.LogError("Failed to find method XUiC_RecipeList::RefreshRecipes sub method insertion point.");
      return false;
    }

    il = wtg.Body.GetILProcessor();
    il.InsertBefore(insertionPoint, il.Create(OpCodes.Ldarg_0));
    il.InsertBefore(insertionPoint, il.Create(OpCodes.Call, getWS));

    return true;
  }
}
