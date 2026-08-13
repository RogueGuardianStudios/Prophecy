using System.Runtime.CompilerServices;

// Lets the EditMode suite reach the few members that are internal because shipping code should
// not call them — HitResolver.ResolveWithoutBroadphase being the one that matters. The alternative
// was making them public and defending them with a comment, which is not a defence.
[assembly: InternalsVisibleTo("Rokkan.Prophecy.Tests")]
