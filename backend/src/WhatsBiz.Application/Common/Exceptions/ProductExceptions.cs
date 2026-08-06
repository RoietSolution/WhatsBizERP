namespace WhatsBiz.Application.Common.Exceptions;

public sealed class EntityNotFoundException(string message) : ApplicationLayerException(message);
public sealed class BusinessRuleException(string message) : ApplicationLayerException(message);
