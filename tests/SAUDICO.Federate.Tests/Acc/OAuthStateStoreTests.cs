using System;
using SAUDICO.Federate.ACC.OAuthState;
using Xunit;

namespace SAUDICO.Federate.Tests.Acc;

public sealed class OAuthStateStoreTests
{
    [Fact]
    public void CorrectState_IsConsumedOnce()
    {
        InMemoryOAuthStateStore store = new InMemoryOAuthStateStore();
        store.Begin(new OAuthAuthorizationState { State = "abc", CodeVerifier = "verifier", CreatedAtUtc = DateTime.UtcNow });

        bool first = store.TryConsume("abc", out OAuthAuthorizationState? matched);
        bool second = store.TryConsume("abc", out _);

        Assert.True(first);
        Assert.NotNull(matched);
        Assert.False(second);
    }

    [Fact]
    public void WrongState_IsRejected()
    {
        InMemoryOAuthStateStore store = new InMemoryOAuthStateStore();
        store.Begin(new OAuthAuthorizationState { State = "abc", CodeVerifier = "verifier", CreatedAtUtc = DateTime.UtcNow });

        bool result = store.TryConsume("wrong", out OAuthAuthorizationState? matched);

        Assert.False(result);
        Assert.Null(matched);
    }

    [Fact]
    public void NoPendingAttempt_IsRejected()
    {
        InMemoryOAuthStateStore store = new InMemoryOAuthStateStore();

        bool result = store.TryConsume("anything", out _);

        Assert.False(result);
    }
}
