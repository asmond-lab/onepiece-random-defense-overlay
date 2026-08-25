using OrandOverlay;

var catalog = new DataCatalog();
catalog.Load(loadCarryPolicy: false);
var generated = GoalCarryPolicy.GenerateCanonical(catalog,
    AppContext.BaseDirectory);
if (args is ["--check", var path])
{
    if (!File.Exists(path) ||
        !File.ReadAllText(path).Equals(generated, StringComparison.Ordinal))
        return 1;
    Console.WriteLine("GOAL_CARRY_POLICY_OK");
    return 0;
}

Console.Write(generated);
return 0;
