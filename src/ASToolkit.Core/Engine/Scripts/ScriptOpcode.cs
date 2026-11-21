namespace ASToolkit.Core.Engine.Scripts;

/// <summary>
/// Specifies the instructions of compiled script code.
/// </summary>
public enum ScriptOpcode : byte
{
    /// <summary>
    /// No instruction.
    /// </summary>
    None = 0,

    /// <summary>
    /// Push a number.
    /// </summary>
    PushNumber = 1,

    /// <summary>
    /// Push a text stored after the opcode.
    /// </summary>
    PushText = 2,

    /// <summary>
    /// Negate the top value.
    /// </summary>
    Negate = 3,

    /// <summary>
    /// Invert the bits of the top value.
    /// </summary>
    BitwiseNot = 4,

    /// <summary>
    /// Push 1 for a zero top value and 0 otherwise.
    /// </summary>
    LogicalNot = 5,

    /// <summary>
    /// Divide.
    /// </summary>
    Divide = 6,

    /// <summary>
    /// Take the remainder.
    /// </summary>
    Modulo = 7,

    /// <summary>
    /// Add numbers or join texts.
    /// </summary>
    Add = 8,

    /// <summary>
    /// Subtract.
    /// </summary>
    Subtract = 9,

    /// <summary>
    /// Apply a bitwise exclusive or.
    /// </summary>
    Xor = 10,

    /// <summary>
    /// Apply a bitwise or.
    /// </summary>
    Or = 11,

    /// <summary>
    /// Apply a bitwise and.
    /// </summary>
    And = 12,

    /// <summary>
    /// Compare for equality.
    /// </summary>
    Equal = 13,

    /// <summary>
    /// Apply a logical or of both values.
    /// </summary>
    LogicalOr = 14,

    /// <summary>
    /// Compare for greater.
    /// </summary>
    Greater = 15,

    /// <summary>
    /// Compare for less.
    /// </summary>
    Less = 16,

    /// <summary>
    /// Compare for greater or equal.
    /// </summary>
    GreaterOrEqual = 17,

    /// <summary>
    /// Compare for less or equal.
    /// </summary>
    LessOrEqual = 18,

    /// <summary>
    /// Multiply.
    /// </summary>
    Multiply = 19,

    /// <summary>
    /// Compare for inequality.
    /// </summary>
    NotEqual = 20,

    /// <summary>
    /// Apply a logical and of both values.
    /// </summary>
    LogicalAnd = 21,

    /// <summary>
    /// Shift right and keep the sign.
    /// </summary>
    ShiftRight = 22,

    /// <summary>
    /// Shift left.
    /// </summary>
    ShiftLeft = 23,

    /// <summary>
    /// Jump when the top value is zero.
    /// </summary>
    JumpIfFalse = 24,

    /// <summary>
    /// End a statement and carry its line.
    /// </summary>
    EndStatement = 25,

    /// <summary>
    /// Drop the top value.
    /// </summary>
    Pop = 26,

    /// <summary>
    /// Jump.
    /// </summary>
    Jump = 28,

    /// <summary>
    /// Jump when the top value is zero, and jump over the whole statement from then on when it is not zero.
    /// </summary>
    JumpIfFalseOnce = 29,

    /// <summary>
    /// Call a function of the script.
    /// </summary>
    Call = 30,

    /// <summary>
    /// Return from a function.
    /// </summary>
    Return = 31,

    /// <summary>
    /// Push a variable, then increment it.
    /// </summary>
    PostIncrement = 32,

    /// <summary>
    /// Push a variable, then decrement it.
    /// </summary>
    PostDecrement = 33,

    /// <summary>
    /// Increment a variable, then push it.
    /// </summary>
    PreIncrement = 34,

    /// <summary>
    /// Decrement a variable, then push it.
    /// </summary>
    PreDecrement = 35,

    /// <summary>
    /// Push a variable.
    /// </summary>
    Load = 36,

    /// <summary>
    /// Push the address of a text variable.
    /// </summary>
    LoadTextAddress = 37,

    /// <summary>
    /// Store the top value in a variable.
    /// </summary>
    Assign = 38,

    /// <summary>
    /// Apply an operator to a variable and the top value.
    /// </summary>
    CompoundAssign = 39,

    /// <summary>
    /// Take the top value as the index of the next variable instruction.
    /// </summary>
    SetIndex = 40,

    /// <summary>
    /// Convert the top value to text (Alien Shooter 2).
    /// </summary>
    CastString = 41,

    /// <summary>
    /// Convert the top value to a number (Alien Shooter 2).
    /// </summary>
    CastInt = 42,

    /// <summary>
    /// Convert the top value to a sprite handle (Alien Shooter 2).
    /// </summary>
    CastSprite = 43,

    /// <summary>
    /// Jump when the top value is zero and keep it on the stack (Alien Shooter 2).
    /// </summary>
    AndJump = 44,

    /// <summary>
    /// Jump when the top value is not zero and keep it on the stack (Alien Shooter 2).
    /// </summary>
    OrJump = 45
}
