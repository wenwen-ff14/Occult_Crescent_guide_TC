using CrescentCompass.Core;

internal static class PhantomMacroIconChecks
{
    internal static void Run(Action<bool, string> check)
    {
        foreach (var job in PhantomJobs.All)
        {
            foreach (var command in new[] { job.Macro, $"/crescent job {job.Name}", $" /CRESCENT\tJOB \"{job.EnglishName}\" " })
            {
                string[] lines = ["", command, " "];
                var original = lines.ToArray();
                check(PhantomMacroIcons.Replacement(PhantomMacroIcons.DefaultIcon, lines) == job.IconId, "Default M maps to the job referenced by the macro");
                check(lines.SequenceEqual(original), "Icon matching never rewrites macro text");
            }
            check(PhantomMacroIcons.Replacement(job.IconId, [job.Macro]) is null, "Already matched icon does not trigger repeated saves");
            check(PhantomMacroIcons.Replacement(66101, [job.Macro]) is null, "Explicitly chosen non-phantom icon is preserved");
        }
        string[][] unsupported = [[], [" "], ["/crescent job"], ["/crescent job 13"], ["/crescent job 騎"],
            ["/echo /crescent job 1"], ["/crescent jobs"], ["/crescent jobicons"], ["/crescent2 job 1"],
            ["/crescent job 1", "/crescent job 2"], ["/crescent job 1", "/echo ready"],
            ["/micon 騎士 classjob", "/crescent job 1"], ["/crescent job 1 <wait.1>"]];
        foreach (var lines in unsupported)
            check(PhantomMacroIcons.Replacement(PhantomMacroIcons.DefaultIcon, lines) is null, "Unrelated, ambiguous or multi-command macros remain unchanged");
        check(PhantomMacroIcons.Replacement(PhantomJobs.Find(1)!.IconId, ["/crescent job 12"]) == PhantomJobs.Find(12)!.IconId,
            "Editing a job command updates its previous phantom icon to the new job");
        check(PhantomMacroIcons.Replacement(0, ["/crescent job 1"]) is null, "Uninitialized macro icon is left alone");
        var copy = Enumerable.Repeat("", 15).ToArray(); copy[14] = "/crescent job 0";
        check(PhantomMacroIcons.Replacement(PhantomMacroIcons.DefaultIcon, copy) == PhantomJobs.Find(0)!.IconId, "Freelancer at last macro line works with blank padding");
    }
}
