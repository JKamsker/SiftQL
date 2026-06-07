namespace SiftQL.Generators.Tests;

internal static class FilterSchemaRuntimeEventSource
{
    public const string Text = """
        using System;
        using SiftQL;

        namespace SiftQL
        {
            public sealed record ActorRef(long ObjectId, string Region);

            public sealed record WorldActionEvent(
                Guid EventId,
                ActorRef Actor,
                int Quantity,
                string Action) : IFilterSubject;
        }

        namespace SiftQL.Input
        {
            public abstract record UiEvent(int WindowId);

            public sealed record ClientPointerEvent(
                int WindowId,
                bool OverOverlay,
                int X,
                int Y) : UiEvent(WindowId), IFilterSubject;
        }

        namespace SiftQL.Dtos.Character
        {
            public sealed record AvatarSnapshot(
                long AvatarId,
                string Name,
                int Level) : IFilterSubject;
        }
        """;
}
