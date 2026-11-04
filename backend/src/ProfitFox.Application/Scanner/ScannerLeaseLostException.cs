namespace ProfitFox.Application.Scanner;

/// <summary>Another scanner took the stream over; this one stops writing.</summary>
public sealed class ScannerLeaseLostException() : Exception("Another scanner holds the stream now.");