// OverrideAudit.cs
//
// Which of IKMA's patches would NOT fire for a subclass? (Session 32.)
// Run it with run_override_audit.sh, which builds the list of patched methods
// from IKMA's own source first.
//
// WHY. A Harmony patch on a base method runs only when that exact method
// runs. If a subclass OVERRIDES it and never calls the base, the patch is
// silently skipped for that subclass - the trap that kept the Act 1 card
// choice screen dead for six sessions. This reads Assembly-CSharp.dll (the
// game's code, read-only, never copied) and, for every patched method,
// lists every class deriving from the patched type that overrides it, and
// whether the override calls a base version.
//
// "calls base" is found in the IL (the compiled instructions): a direct
// call to a same-named method on another type, or, for coroutines, the
// compiler's hidden "<>n__" helper that makes that call from inside the
// generated enumerator. It only proves A base is called - if the parent is
// itself an override that skips ITS base, follow the chain by eye.
//
// Output: one line per override, tab-separated:
//   PatchedType.Method   Namespace.OverridingType   calls base | NO base call

using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

static class OverrideAudit
{
    static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("usage: OverrideAudit <Assembly-CSharp.dll> <targets.txt: 'Type Method' per line>");
            return 2;
        }
        var module = AssemblyDefinition.ReadAssembly(args[0]).MainModule;
        var types = module.GetTypes().ToList();

        foreach (string line in File.ReadAllLines(args[1]))
        {
            string[] p = line.Split(' ');
            if (p.Length != 2) continue;
            string typeName = p[0], method = p[1];
            foreach (TypeDefinition t in types.Where(t => Derives(t, typeName)))
                foreach (MethodDefinition m in t.Methods.Where(m => m.Name == method && m.IsVirtual && !m.IsNewSlot))
                    Console.WriteLine($"{typeName}.{method}\t{t.Namespace}.{t.Name}\t{(CallsBase(t, m) ? "calls base" : "NO base call")}");
        }
        return 0;
    }

    static bool Derives(TypeDefinition t, string baseName)
    {
        TypeReference b = t.BaseType;
        while (b != null)
        {
            if (b.Name == baseName) return true;
            TypeDefinition d;
            try { d = b.Resolve(); } catch { return false; }   // a type in another DLL
            if (d == null) return false;
            b = d.BaseType;
        }
        return false;
    }

    static bool CallsSameName(MethodDefinition body, string name, string ownType)
        => body.HasBody && body.Body.Instructions.Any(i =>
               i.OpCode == OpCodes.Call && i.Operand is MethodReference r
               && r.Name == name && r.DeclaringType.Name != ownType);

    static bool CallsBase(TypeDefinition t, MethodDefinition m)
    {
        if (CallsSameName(m, m.Name, t.Name)) return true;
        // Coroutine: the body lives in a nested "<Method>d__N" class, and a
        // base call is routed through a "<>n__N" helper on the outer class.
        var helpers = t.Methods.Where(h => h.Name.StartsWith("<>n__") && CallsSameName(h, m.Name, t.Name))
                               .Select(h => h.Name).ToList();
        foreach (TypeDefinition nested in t.NestedTypes.Where(n => n.Name.Contains("<" + m.Name + ">")))
            foreach (MethodDefinition nm in nested.Methods.Where(x => x.HasBody))
                if (nm.Body.Instructions.Any(i => i.Operand is MethodReference r
                        && (helpers.Contains(r.Name) || (r.Name == m.Name && r.DeclaringType.Name != t.Name))))
                    return true;
        return false;
    }
}
