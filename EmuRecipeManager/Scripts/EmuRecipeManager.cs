using System;
using System.Collections.Generic;

/// <summary>
/// TormentedEmu 2018 tormentedemu@gmail.com
/// A class to retrieve a list of recipes specific to the current workstation the player is viewing
/// </summary>
public static class EmuRecipeManager
{
  static EmuRecipeManager()
  {
  }

  /// <summary>
  /// Gets all recipes by the currently opened workstation in the player ui
  /// </summary>
  /// <returns></returns>
  public static List<Recipe> GetRecipes()
  {
    List<Recipe> recipes = new List<Recipe>();

    var localPlayer = GameManager.Instance.World.GetPrimaryPlayer();
    if (localPlayer == null)
    {
      Log.Error("EmuRecipeManager::GetRecipes failed to find the local player");
      return recipes;
    }

    var ui = localPlayer.PlayerUI.xui;
    if (ui == null)
    {
      Log.Error("EmuRecipeManager::GetRecipes failed to find the local player UI");
      return recipes;
    }

    var curWS = ui.currentWorkstation;
    if (string.IsNullOrEmpty(curWS))
    {
      curWS = ""; // empty means its crafted in the backpack
    }

    for (int idx = 0; idx < CraftingManager.MasterRecipeList.Count; idx++)
    {
      Recipe r = CraftingManager.MasterRecipeList[idx];
      if (r.wildcardForgeCategory)
        continue;

      if (CraftingManager.MasterLockedRecipeList.Contains(r.GetName()) && !CraftingManager.UnlockedRecipeList.Contains(r.GetName()))
        continue;

      if (r.craftingArea == curWS)
      {
        recipes.Add(r);
      }
    }

    return recipes;
  }

  /// <summary>
  /// Gets all recipes for the supplied workstation
  /// </summary>
  /// <param name="workstation">The name of the station to search recipes for</param>
  /// <returns></returns>
  public static List<Recipe> GetRecipesStation(string workstation)
  {
    List<Recipe> recipes = new List<Recipe>();

    for (int idx = 0; idx < CraftingManager.MasterRecipeList.Count; idx++)
    {
      Recipe r = CraftingManager.MasterRecipeList[idx];
      if (r.wildcardForgeCategory)
        continue;

      if (CraftingManager.MasterLockedRecipeList.Contains(r.GetName()) && !CraftingManager.UnlockedRecipeList.Contains(r.GetName()))
        continue;

      if (r.craftingArea == workstation)
      {
        recipes.Add(r);
      }
    }

    return recipes;
  }
}