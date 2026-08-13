using System.Runtime.CompilerServices;

// Lets the EditMode suite reach the few members that are internal because shipping code should
// not call them — HitResolver.ResolveWithoutBroadphase being the one that matters. The alternative
// was making them public and defending them with a comment, which is not a defence.
[assembly: InternalsVisibleTo("Rokkan.Prophecy.Tests")]

// This assembly IS the sim/presentation split, compiled: it references no presentation, no world,
// no UI, and nothing here can reach a MonoBehaviour without the compiler objecting. The reflection
// gates still guard what an assembly boundary cannot — engine-object types inside, frame-time
// calls — but the DIRECTION of the dependency stopped being a convention the day this file's
// asmdef landed.
