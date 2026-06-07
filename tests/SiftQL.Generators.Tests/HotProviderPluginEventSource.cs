using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SiftQL.Generators.Tests;

internal static class HotProviderPluginEventSource
{
    public static SyntaxTree Tree() =>
        CSharpSyntaxTree.ParseText("""
            using System;
            using SiftQL;

            namespace Plugin.Events;

            public sealed record SkillRef(int Id, int Level);

            public enum PluginEventKind { Unknown, Hit }

            public sealed record PluginOwnedEvent(
                Guid EventId,
                long CharacterId,
                SkillRef Skill,
                PluginEventKind Kind,
                int[] Tokens) : IFilterSubject;
            """, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
}
