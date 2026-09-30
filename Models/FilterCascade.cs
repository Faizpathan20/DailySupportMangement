using System.Collections.Generic;
using System.Linq;

namespace Master.Models;

// ==========================================
// CASCADING FILTER HELPERS
//
// The Dashboard and the Reports share the same
// three relationship filters (State / User /
// Client). They are cascading, driven by the
// real foreign keys:
//
//   States.UserId       -> LoginUsers.Id
//   ClientMaster.UserId -> LoginUsers.Id
//   ClientMaster.StateId-> States.Id
//
// So the hierarchy is  User > State > Client.
//
// "User" sits at the top because it is the only
// entity that both States and ClientMaster point
// at. Selecting a value anywhere must:
//
//   1. narrow the options of every other filter
//      to what is actually reachable, and
//   2. clear any sibling selection that the new
//      value makes impossible.
//
// These helpers are the single source of truth
// for rule (2). The browser runs the identical
// rules for rule (1) so the UI can narrow
// instantly without a round-trip, and the server
// still re-runs (2) on every request so a hand
// crafted URL can never produce a combination
// that the dropdowns would never allow.
// ==========================================

// The normalized (always consistent) filter
// selection produced by FilterCascade.Normalize.
public sealed class CascadeSelection
{
    public int? StateId { get; set; }

    public int? UserId { get; set; }

    public int? ClientId { get; set; }
}


public static class FilterCascade
{
    // Drops selections that cannot exist together.
    public static CascadeSelection Normalize(
        int? stateId,
        int? userId,
        int? clientId,
        List<LookupOptionViewModel>? states,
        List<LookupOptionViewModel>? users,
        List<LookupOptionViewModel>? clients)
    {
        states ??= new List<LookupOptionViewModel>();
        users ??= new List<LookupOptionViewModel>();
        clients ??= new List<LookupOptionViewModel>();

        // 1) Unknown / stale ids never survive.
        if (!IsKnown(stateId, states))
        {
            stateId = null;
        }

        if (!IsKnown(userId, users))
        {
            userId = null;
        }

        if (!IsKnown(clientId, clients))
        {
            clientId = null;
        }

        // 2) A user narrows the states and the
        //    clients down to what they own.
        if (userId.HasValue)
        {
            if (stateId.HasValue
                && OwnerUserId(stateId.Value, states) != userId)
            {
                stateId = null;
            }

            if (clientId.HasValue
                && clients
                    .First(c => c.Id == clientId.Value)
                    .ParentId != userId)
            {
                clientId = null;
            }
        }

        // 3) A state belongs to exactly one user.
        //    Without an owner it cannot constrain
        //    anything, so it is dropped as well.
        if (stateId.HasValue)
        {
            int? owner = OwnerUserId(stateId.Value, states);

            if (!owner.HasValue)
            {
                stateId = null;
            }
        }

        // 4) A client must sit inside the selected
        //    state (and the selected user, checked
        //    above).
        if (stateId.HasValue
            && clientId.HasValue
            && clients
                .First(c => c.Id == clientId.Value)
                .GroupId != stateId)
        {
            clientId = null;
        }

        return new CascadeSelection
        {
            StateId = stateId,
            UserId = userId,
            ClientId = clientId
        };
    }


    // ---- Option lists the browser mirrors ----


    // States reachable from the selected user.
    public static List<LookupOptionViewModel> AllowedStates(
        int? userId,
        List<LookupOptionViewModel>? states)
    {
        if (states == null)
        {
            return new List<LookupOptionViewModel>();
        }

        return userId.HasValue
            ? states.Where(s => s.ParentId == userId).ToList()
            : states.ToList();
    }


    // Users reachable from the selected state: a
    // state is owned by a single user.
    public static List<LookupOptionViewModel> AllowedUsers(
        int? stateId,
        List<LookupOptionViewModel>? states,
        List<LookupOptionViewModel>? users)
    {
        if (users == null)
        {
            return new List<LookupOptionViewModel>();
        }

        if (!stateId.HasValue || states == null)
        {
            return users.ToList();
        }

        int? owner = OwnerUserId(stateId.Value, states);

        return owner.HasValue
            ? users.Where(u => u.Id == owner.Value).ToList()
            : users.ToList();
    }


    // Clients reachable from the selected user and
    // the selected state.
    public static List<LookupOptionViewModel> AllowedClients(
        int? userId,
        int? stateId,
        List<LookupOptionViewModel>? clients)
    {
        if (clients == null)
        {
            return new List<LookupOptionViewModel>();
        }

        IEnumerable<LookupOptionViewModel> query = clients;

        if (userId.HasValue)
        {
            query = query.Where(c => c.ParentId == userId);
        }

        if (stateId.HasValue)
        {
            query = query.Where(c => c.GroupId == stateId);
        }

        return query.ToList();
    }


    // ---- internals ----

    private static bool IsKnown(
        int? id,
        List<LookupOptionViewModel>? options)
    {
        return id.HasValue
            && options != null
            && options.Any(o => o.Id == id.Value);
    }


    private static int? OwnerUserId(
        int stateId,
        List<LookupOptionViewModel>? states)
    {
        LookupOptionViewModel? state =
            states?.FirstOrDefault(s => s.Id == stateId);

        return state?.ParentId;
    }
}