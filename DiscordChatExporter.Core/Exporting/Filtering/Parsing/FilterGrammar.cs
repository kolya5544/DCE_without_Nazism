﻿using DiscordChatExporter.Core.Utils.Extensions;
using Superpower;
using Superpower.Parsers;

namespace DiscordChatExporter.Core.Exporting.Filtering.Parsing;

internal static class FilterGrammar
{
    private static readonly TextParser<char> EscapedCharacter =
        Character.EqualTo('\\').IgnoreThen(Character.AnyChar);

    private static readonly TextParser<string> QuotedString =
        from open in Character.In('"', '\'')
        from value in Parse.OneOf(EscapedCharacter, Character.Except(open)).Many().Text()
        from close in Character.EqualTo(open)
        select value;

    private static readonly TextParser<string> UnquotedString =
        Parse.OneOf(
            EscapedCharacter,
            Character.Matching(
                c =>
                    !char.IsWhiteSpace(c) &&
                    // Avoid all special tokens used by the grammar
                    c is not ('(' or ')' or '"' or '\'' or '-' or '~' or '|' or '&'),
                "any character except whitespace or `(`, `)`, `\"`, `'`, `-`, `|`, `&`"
            )
        ).AtLeastOnce().Text();

    private static readonly TextParser<string> String =
        Parse.OneOf(QuotedString, UnquotedString).Named("text string");

    private static readonly TextParser<MessageFilter> ContainsFilter =
        String.Select(v => (MessageFilter) new ContainsMessageFilter(v));

    private static readonly TextParser<MessageFilter> FromFilter = Span
        .EqualToIgnoreCase("from:")
        .IgnoreThen(String)
        .Select(v => (MessageFilter) new FromMessageFilter(v))
        .Named("from:<value>");

    private static readonly TextParser<MessageFilter> MentionsFilter = Span
        .EqualToIgnoreCase("mentions:")
        .IgnoreThen(String)
        .Select(v => (MessageFilter) new MentionsMessageFilter(v))
        .Named("mentions:<value>");

    private static readonly TextParser<MessageFilter> ReactionFilter = Span
        .EqualToIgnoreCase("reaction:")
        .IgnoreThen(String)
        .Select(v => (MessageFilter) new ReactionMessageFilter(v))
        .Named("reaction:<value>");

    private static readonly TextParser<MessageFilter> HasFilter = Span
        .EqualToIgnoreCase("has:")
        .IgnoreThen(Parse.OneOf(
            Span.EqualToIgnoreCase("link").IgnoreThen(Parse.Return(MessageContentMatchKind.Link)),
            Span.EqualToIgnoreCase("embed").IgnoreThen(Parse.Return(MessageContentMatchKind.Embed)),
            Span.EqualToIgnoreCase("file").IgnoreThen(Parse.Return(MessageContentMatchKind.File)),
            Span.EqualToIgnoreCase("video").IgnoreThen(Parse.Return(MessageContentMatchKind.Video)),
            Span.EqualToIgnoreCase("image").IgnoreThen(Parse.Return(MessageContentMatchKind.Image)),
            Span.EqualToIgnoreCase("sound").IgnoreThen(Parse.Return(MessageContentMatchKind.Sound)),
            Span.EqualToIgnoreCase("pin").IgnoreThen(Parse.Return(MessageContentMatchKind.Pin))
        ))
        .Select(k => (MessageFilter) new HasMessageFilter(k))
        .Named("has:<value>");

    private static readonly TextParser<MessageFilter> PrimitiveFilter = Parse.OneOf(
        FromFilter,
        MentionsFilter,
        ReactionFilter,
        HasFilter,
        ContainsFilter
    );

    private static readonly TextParser<MessageFilter> GroupedFilter =
        from open in Character.EqualTo('(')
        from content in Parse.Ref(() => ChainedFilter!).Token()
        from close in Character.EqualTo(')')
        select content;

    private static readonly TextParser<MessageFilter> NegatedFilter = Character
        // Dash is annoying to use from CLI due to conflicts with options, so we provide tilde as an alias
        .In('-', '~')
        .IgnoreThen(Parse.OneOf(GroupedFilter, PrimitiveFilter))
        .Select(f => (MessageFilter) new NegatedMessageFilter(f));

    private static readonly TextParser<MessageFilter> ChainedFilter = Parse.Chain(
        // Operator
        Parse.OneOf(
            // Explicit operator
            Character.In('|', '&').Token().Try(),
            // Implicit operator (resolves to 'and')
            Character.WhiteSpace.AtLeastOnce().IgnoreThen(Parse.Return(' '))
        ),
        // Operand
        Parse.OneOf(
            NegatedFilter,
            GroupedFilter,
            PrimitiveFilter
        ),
        // Reducer
        (op, left, right) => op switch
        {
            '|' => new BinaryExpressionMessageFilter(left, right, BinaryExpressionKind.Or),
            _ => new BinaryExpressionMessageFilter(left, right, BinaryExpressionKind.And)
        }
    );

    public static readonly TextParser<MessageFilter> Filter =
        ChainedFilter.Token().AtEnd();
}