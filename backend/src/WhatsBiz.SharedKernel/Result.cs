namespace WhatsBiz.SharedKernel;

public sealed class Result
{
    private Result(bool isSuccess, Failure? failure) => (IsSuccess, Failure) = (isSuccess, failure);
    public bool IsSuccess { get; }
    public Failure? Failure { get; }
    public static Result Success() => new(true, null);
    public static Result FailureResult(Failure failure) => new(false, failure);
}

public sealed record Failure(string Code, string Description);
