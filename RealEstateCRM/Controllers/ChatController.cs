using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;

namespace RealEstateCRM.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChatController : ControllerBase
{
    // Simple in-memory KB. Expand as needed.
    private static readonly List<KBItem> KnowledgeBase =
    [
        new("deals pipeline stages",
            ["deal","deals","pipeline","stage","status","kanban","board"],
            """
The Deals board uses these statuses (custom labels may appear if renamed locally):
New ? Offer Made ? Negotiation ? Contract Draft ? Under Contract ? Clear To Close ? Closed (or Fell Through).
Drag-and-drop rules: 
• From New only to Offer Made or Negotiation. 
• Under Contract only from Contract Draft (with confirmation & deadlines initialization).
Offers are set in Negotiation; accepted offers advance progress.
"""),
        new("offers & offer modal",
            ["offer","offers","amount","negotiation","set offer","proposed","accept","decline"],
            """
Offers are created from a deal card in Negotiation via the Set Offer button.
The modal enforces an allowed range (90% of list price up to 100%).
After saving, the card can move toward Contract Draft. Proposed offers can be Accepted or Declined inline.
"""),
        new("deadlines & under contract",
            ["deadline","deadlines","under contract","inspection","appraisal","loan","closing","timeline"],
            """
When a deal enters Under Contract the system tracks key deadlines: Inspection, Appraisal, Loan Commitment, Closing.
You can view/edit them in the Deal Details modal. Each deadline shows status colors (overdue, upcoming, completed).
"""),
        new("properties basics",
            ["property","properties","listing","price","type","sell","rent","bedrooms","bathrooms","area"],
            """
Properties have title, address, price, type (e.g. Sell), bedrooms, bathrooms, area and optional image.
Selecting a property while creating a deal auto-populates constraints for initial Offer Amount (90%–100% of list).
"""),
        new("contacts vs leads",
            ["contact","contacts","lead","leads","convert","conversion"],
            """
Leads are preliminary records. A lead can be converted to a Contact (using the Convert action in Leads).
Contacts include richer fields: Occupation, Salary, Last Contacted, Notes, Documents, and email interactions.
"""),
        new("last contacted & emailing",
            ["last contacted","email","send email","contact email","message"],
            """
You can email a Contact from the Contact Details modal; sending an email updates Last Contacted.
Emails support file attachments stored under that contact's Documents list.
"""),
        new("documents for contacts",
            ["document","documents","upload","file","attachment"],
            """
In Contact Details you can upload documents; they are listed with filename, size, and timestamp. They can also be attached to outgoing emails.
"""),
        new("agents & roles",
            ["agent","agents","broker","role","manage agents","lock","unlock","resend confirmation"],
            """
Brokers can create Agent accounts (from user dropdown ? Create Agent Account) and manage them in Manage Agents.
Actions include Lock/Unlock, Resend Confirmation, Email Agent, and Delete.
Agents (non-Broker) see Pending Assignments instead of Properties. Brokers also access All Deals and Archives.
"""),
        new("notifications",
            ["notification","notifications","bell","mark read"],
            """
The top bar bell shows unread notification count (polls periodically). You can open the dropdown to review recent notifications and mark all as read.
"""),
        new("authentication & account",
            ["login","logout","sign in","password","account","manage account","register"],
            """
Authentication uses ASP.NET Identity. Users can manage their profile & password via Manage Account. Brokers may have extended navigation (All Deals, Manage Agents, Archives).
"""),
        new("renaming columns",
            ["rename","column","stage name","custom column","kanban label"],
            """
On the Deals board (non 'All Deals' view) you can rename a column by double-clicking its header or using the edit icon. Names persist in localStorage for that browser.
"""),
        new("filter & search deals",
            ["filter","search","agent filter","type filter","board search","hide empty"],
            """
Deals board supports: 
• Agent filter (in All Deals). 
• Search box (title, property, client/agent). 
• Type filter (property type). 
• Hide empty columns toggle. 
Counts & sums update dynamically.
"""),
        new("security & scope limits",
            ["scope","policy","out of scope","external","weather","news","general knowledge"],
            """
The helpdesk chatbot only answers about this CRM's features: Deals, Offers, Deadlines, Properties, Leads, Contacts, Agents, Notifications, Roles, and UI behaviors. It will not answer unrelated general knowledge.
""")
    ];

    // Disallowed / out-of-scope broad topics
    private static readonly string[] OutOfScopeKeywords =
        ["politic","weather","stock","finance market","movie","news","sport","recipe","math proof","code unrelated"];

    [HttpPost("ask")]
    public ActionResult<ChatReply> Ask([FromBody] ChatAsk request)
    {
        var message = (request.Message ?? "").Trim();
        if (string.IsNullOrWhiteSpace(message))
            return Ok(new ChatReply("Please enter a question about the CRM.", Suggested()));

        // Out-of-scope guard
        var lowered = message.ToLowerInvariant();
        if (OutOfScopeKeywords.Any(k => lowered.Contains(k)))
        {
            return Ok(new ChatReply(
                "I can only help with this CRM (Deals, Offers, Deadlines, Properties, Leads, Contacts, Agents, Notifications, Roles). Rephrase within that scope.",
                Suggested()));
        }

        var best = FindBestMatch(message);
        if (best == null)
        {
            return Ok(new ChatReply(
                "I don’t have an answer for that within the CRM scope. Try asking about: deals pipeline, offers, deadlines, leads vs contacts, or notifications.",
                Suggested()));
        }

        return Ok(new ChatReply(best.Answer, Suggested(best)));
    }

    private KBItem? FindBestMatch(string question)
    {
        var q = question.ToLowerInvariant();

        // Basic scoring: keyword hits + semantic similarity via token overlap length
        KBItem? best = null;
        double bestScore = 0;
        foreach (var item in KnowledgeBase)
        {
            double score = 0;
            foreach (var kw in item.Keywords)
            {
                if (q.Contains(kw)) score += 3;
            }

            // Token overlap
            var qTokens = Tokenize(q);
            var titleTokens = Tokenize(item.Title);
            var overlap = qTokens.Intersect(titleTokens, StringComparer.OrdinalIgnoreCase).Count();
            score += overlap * 1.5;

            // Fuzzy partial (very small heuristic)
            if (Regex.IsMatch(q, Regex.Escape(item.Title.Split(' ').First()), RegexOptions.IgnoreCase))
                score += 1;

            if (score > bestScore)
            {
                bestScore = score;
                best = item;
            }
        }

        // Threshold to avoid random matches
        return bestScore >= 3 ? best : null;
    }

    private static IEnumerable<string> Tokenize(string s) =>
        Regex.Split(s, @"[^a-zA-Z0-9]+", RegexOptions.Compiled)
             .Where(t => t.Length > 0);

    private static string[] Suggested(KBItem? primary = null)
    {
        var baseSet = new[]
        {
            "How do deal stages work?",
            "How do I set or accept an offer?",
            "What happens when a deal moves Under Contract?",
            "Difference between Leads and Contacts?",
            "How do I email a contact?",
            "How do deadlines work?"
        };

        if (primary == null) return baseSet;

        // Slightly tailor suggestions
        if (primary.Title.Contains("offer", StringComparison.OrdinalIgnoreCase))
            return new[]
            {
                "How do deal stages work?",
                "What happens after an offer is accepted?",
                "How do deadlines work?"
            };

        if (primary.Title.Contains("deadline", StringComparison.OrdinalIgnoreCase))
            return new[]
            {
                "How do deal stages work?",
                "How do I set or accept an offer?",
                "How do I view deal details?"
            };

        return baseSet;
    }

    public record ChatAsk(string Message);
    public record ChatReply(string Reply, string[] Suggested);
    private record KBItem(string Title, string[] Keywords, string Answer);
}