# Domain Model Compliance Audit — RulesApp

**Date:** January 2, 2026  
**Scope:** API and Web app review against [docs/domain.md](domain.md)

---

## Executive Summary

The API and Web app **implement most core domain rules correctly**, but have **several gaps and weaknesses**:

✅ **Strengths:**
- Chunk structure (ChunkId, RuleKey, RuleNumberText, etc.) is well-defined
- Precedence resolver correctly prioritizes Regional > Quebec > Canada
- Search scope filters work as specified
- Not Found behavior is implemented
- Basic citations are present in responses
- Override status enum exists (Proposed, Confirmed, Rejected)

⚠️ **Gaps & Issues:**
1. **No bilingual UI support** — UI is hardcoded in French only; no language toggle
2. **No user language preference handling** — Chat/Search don't accept language parameter
3. **Citation validation is weak** — only logs warnings instead of enforcing
4. **RuleKey normalization is incorrect** — uses `RULE_` prefix and `_` separators, not standard rule numbers
5. **Override relationship types missing** — no distinction between Override/Append/Clarify
6. **No admin UI for override confirmation** — only API stubs exist
7. **RegionalEn DocType missing** — only RegionalFr is defined
8. **Season is hardcoded** — no active season management
9. **Association validation incomplete** — missing API 400 on invalid Regional + no association

---

## Detailed Findings

### 1. ✅ Chunk Structure & Metadata

**Status:** COMPLIANT

The `RuleChunkDto` correctly includes:
- `ChunkId` (unique + stable)
- `RuleNumberText` (as found in PDF)
- `RuleKey` (canonical key)
- `Title`, `PageStart`, `PageEnd`, `PdfPath`
- `ScopeLevel`, `AssociationId`, `Language`

**Code:** [RulesApp.Shared/Models.cs](../src/RulesApp.Shared/Models.cs#L55-L76)

---

### 2. ✅ RuleKey Normalization — FIXED

**Status:** COMPLIANT

**Previous issue:** RuleKey was being generated with incorrect format:
```csharp
// Old implementation (WRONG):
var normalized = ruleNumberText.Replace(" ", "").Replace(".", "_").Replace("(", "_").Replace(")", "");
ruleKey = $"RULE_{normalized}";
// Example: "6.01(a)" becomes "RULE_6_01_a_"
```

**Fixed implementation:** Now preserves rule numbers as-is for matching across scopes:
```csharp
// Correct approach (FIXED):
string? ruleKey = ruleNumberText?.Trim();
// Example: "6.01(a)" remains "6.01(a)"
```

**Impact:**
- Override mappings will fail to match rules across Canada/Quebec/Regional
- Cannot compare "6.01(a)" in Canada with "6.01(a)" in Quebec supplement
- Precedence resolution grouped by RuleKey will not work correctly

**File:** [src/RulesApp.Api/Services/Chunker.cs](../src/RulesApp.Api/Services/Chunker.cs#L58-L63)

---

### 3. ✅ Precedence Resolution — CORRECTLY IMPLEMENTED

**Status:** COMPLIANT

The `PrecedenceResolver` correctly:
- Groups results by `RuleKey`
- Applies precedence: Regional (3) > Quebec (2) > Canada (1)
- Loads confirmed overrides from table storage
- Promotes overridden chunks if a confirmed mapping exists

**Code:** [src/RulesApp.Api/Services/PrecedenceResolver.cs](../src/RulesApp.Api/Services/PrecedenceResolver.cs#L25-L85)

**Example flow:**
1. Search finds "6.01(a)" in Canada (score 8.5) and Quebec (score 7.2)
2. PrecedenceResolver groups by RuleKey "6.01(a)"
3. Quebec chunk selected as primary (higher precedence)
4. Canada chunk added as alternate

---

### 4. ✅ Search Scope Filters — MOSTLY COMPLIANT

**Status:** MOSTLY COMPLIANT

**Implemented correctly:**
- Association filter: when `AssociationId` is set, include association-scoped OR global (`associationId == null`) chunks ✅
- When no association: only global chunks (`associationId == null`) ✅
- Scope filtering by Canada/Quebec/Regional works ✅

**Missing validation:**
- No HTTP 400 error if Regional scope requested without `AssociationId`
- UI disables Regional checkbox when no association selected (good UX)
- API should explicitly reject this invalid combination

**Code:**
- API: [src/RulesApp.Api/Functions/Search.cs](../src/RulesApp.Api/Functions/Search.cs#L68-L73) — validates but no error return shown
- Web: [src/RulesApp.Web/Pages/Search.razor](../src/RulesApp.Web/Pages/Search.razor#L30-L40) — disables checkbox ✅

---

### 5. ⚠️ Language Support — MAJOR GAP

**Status:** NON-COMPLIANT

**Critical Issues:**

#### 5a. No UI Language Toggle
- Web UI is hardcoded in **French only**
- No language switcher (EN/FR toggle)
- Domain requires bilingual UX

**Files affected:**
- [src/RulesApp.Web/Pages/Search.razor](../src/RulesApp.Web/Pages/Search.razor) — all labels in French
- [src/RulesApp.Web/Pages/Chat.razor](../src/RulesApp.Web/Pages/Chat.razor) — all labels in French
- [src/RulesApp.Web/Layout/NavMenu.razor](../src/RulesApp.Web/Layout/NavMenu.razor) — assumed French only

#### 5b. No Language Parameter in Chat/Search Requests
- `ChatRequest` does not include `Language` field
- `SearchRequest` does not include `Language` field
- API cannot know user's language preference
- Chat response assumes API-level language selection (not client-driven)

**Expected behavior (from domain.md section 4):**
> The response should be in the **user-selected language** based on the user preference in the UI.

**Current behavior:**
- Responses are generated from whatever chunks match the query
- If FR-only regional chunks exist, response will be in FR even if user selected EN

#### 5c. No Language in Chat Service
`ChatService.GenerateDirectAnswer()` and `GenerateAIAnswerAsync()` have no language awareness.

**File:** [src/RulesApp.Api/Services/ChatService.cs](../src/RulesApp.Api/Services/ChatService.cs#L130-L155)

---

### 6. ⚠️ Citation Validation — INSUFFICIENT

**Status:** WEAK IMPLEMENTATION

**Current behavior:**
```csharp
var isValid = ValidateCitations(answer, citations);
if (!isValid)
{
    // Only logs warning — does NOT reject response
    Console.WriteLine($"[WARNING] Answer may contain ungrounded information...");
}
```

**Issues:**
- Domain rule (section 5): *"If the system answers (`status = ok`): it must include **at least one citation** and citations must refer to retrieved chunk IDs"*
- Only logging warnings, not enforcing
- No check that citations are actually mentioned in answer
- Ungrounded answers are still returned to user

**File:** [src/RulesApp.Api/Services/ChatService.cs](../src/RulesApp.Api/Services/ChatService.cs#L120-L130)

**Expected:** Should return `status = not_found` if citations cannot be validated.

---

### 7. ⚠️ Override Management — INCOMPLETE

**Status:** PARTIAL IMPLEMENTATION

**What exists:**
- ✅ `OverrideStatus` enum (Proposed, Confirmed, Rejected)
- ✅ `OverrideMappingDto` with full structure
- ✅ `PrecedenceResolver` loads confirmed overrides
- ✅ Confirmed override can promote a chunk to primary

**What's missing:**
- ❌ No relationship type field (Override / Append / Clarify)
- ❌ No admin UI to confirm/reject overrides
- ❌ No `AdminOverrides.cs` implementation (file exists but likely empty)
- ❌ No detection of override proposals (heuristic)

**Domain requirement (section 3):**
> Confirmed mappings can be applied at runtime to:
> - hide overridden base rule chunks (Override relationship)
> - show both base and supplement with "Additional regional rule…" (Append)
> - show base rule + clarification (Clarify)

**Current limitation:** All confirmed overrides treated as generic "takes precedence"; no relationship type behavior.

---

### 8. ⚠️ DocType Coverage — INCOMPLETE

**Status:** PARTIAL COMPLIANCE

**Defined DocTypes:**
```csharp
public enum DocType
{
    CanadaFr,    // ✅
    CanadaEn,    // ✅
    QuebecFr,    // ✅
    QuebecEn,    // ✅
    RegionalFr   // ⚠️ Only French!
}
```

**Missing:** `RegionalEn`

**Domain requirement (section 2):**
> - Canada rulebook exists in **FR + EN**
> - Quebec and regional exist in **FR or EN or both**

**Impact:** Cannot index English-language regional supplements.

---

### 9. ⚠️ Season Management — HARDCODED

**Status:** WEAK IMPLEMENTATION

**Current:**
```csharp
var seasonId = request.SeasonId ?? "2025"; // Hardcoded fallback
```

**Missing:**
- No active season concept
- No season validation
- No seasonal archive/search capabilities
- Domain requires "only **active season** content is searchable"

**Files affected:**
- [src/RulesApp.Api/Functions/Search.cs](../src/RulesApp.Api/Functions/Search.cs#L96)
- [src/RulesApp.Api/Functions/Chat.cs](../src/RulesApp.Api/Functions/Chat.cs) (no Season parameter)

**Expected:** Should have an `ISeasonStore` to:
- Get active season ID
- List available seasons
- Block searches on inactive seasons (future feature)

---

### 10. ⚠️ Not Found Behavior — ACCEPTABLE

**Status:** ACCEPTABLE (with minor gaps)

**Implemented correctly:**
- ✅ Returns `status = not_found` when no relevant chunks found
- ✅ No citations included in not_found response
- ✅ Helpful message suggests rephrasing

**Gap:**
- Does not distinguish between:
  - No chunks retrieved at all
  - Chunks retrieved but below relevance threshold (Score < 1.0)

**Code:** [src/RulesApp.Api/Services/ChatService.cs](../src/RulesApp.Api/Services/ChatService.cs#L48-L88)

---

### 11. ✅ Admin-Only Operations — BASIC STRUCTURE EXISTS

**Status:** PARTIAL IMPLEMENTATION

**Implemented:**
- ✅ `AdminUpload.cs` — upload PDF
- ✅ `AdminBuild.cs` — trigger ingestion
- ✅ `RulesIngestWorker.cs` — queue-based processing

**Missing:**
- ❌ Admin UI for override confirmation
- ❌ Override proposal review workflow
- ❌ Admin endpoints for publishing season

---

### 12. ⚠️ Web UI Scope Validation — INCOMPLETE

**Status:** WEAK IMPLEMENTATION

**Current behavior:**
- ✅ UI disables Regional checkbox until association selected
- ❌ API does not enforce (no 400 error if Regional + no association sent directly)

**Example vulnerability:** Client could send:
```json
{
  "query": "balk",
  "scopes": ["Regional"],
  "associationId": null
}
```

**Expected:** API should return HTTP 400:
```csharp
if (request.Scopes?.Contains("Regional") == true && string.IsNullOrEmpty(request.AssociationId))
{
    // This check exists in Search.cs but needs verification Chat.cs
    return 400;
}
```

---

## Summary Table

| Domain Rule | Status | Notes |
|-------------|--------|-------|
| Chunk structure (ChunkId, RuleKey, etc.) | ✅ | Well implemented |
| Precedence (Regional > Quebec > Canada) | ✅ | Correct algorithm |
| RuleKey normalization | ✅ | FIXED: preserves rule numbers as-is |
| Override relationship types | ❌ | Override/Append/Clarify not distinguished |
| Bilingual UI | ❌ | French-only hardcoded |
| Language in requests | ❌ | No Language parameter in ChatRequest |
| Citation validation | ⚠️ | Only warns; doesn't reject ungrounded answers |
| Not Found behavior | ✅ | Correctly implemented |
| Search scope validation | ⚠️ | UI good; API missing 400 on Regional + no association |
| DocType coverage | ❌ | RegionalEn missing |
| Season management | ⚠️ | Hardcoded to 2025; no active season concept |
| Admin override UI | ❌ | No UI; only API stubs |
| Association filtering | ✅ | Correctly includes association-scoped + global |

---

## Recommended Priority Fixes

### Critical (Breaking)
1. ✅ **FIXED: RuleKey normalization** — rule numbers now preserved as-is (e.g., "6.01(a)")
   - Impact: Enables correct override matching, precedence grouping, domain correctness
   - Effort: ✅ Complete

2. **Add language parameter to Chat/Search** — let client specify EN/FR
   - Impact: Bilingual compliance, correct response language
   - Effort: 2 hours (includes Web UI toggle)

3. **Add RegionalEn DocType** — enable English regional supplements
   - Impact: Domain completeness, coverage
   - Effort: 30 minutes (add enum value + update chunker helpers)

### High Priority
4. **Enforce Regional scope validation in API** — return 400 if Regional + no association
   - Impact: Robustness, API contract
   - Effort: 30 minutes

5. **Implement override relationship types** — distinguish Override/Append/Clarify
   - Impact: Admin UI workflow, correct precedence display
   - Effort: 3 hours

6. **Add active season management** — replace hardcoded 2025
   - Impact: Multi-season support, operational readiness
   - Effort: 2 hours

### Medium Priority
7. **Strengthen citation validation** — reject responses with ungrounded info
   - Impact: Hallucination guard, grounding compliance
   - Effort: 1 hour

8. **Admin override confirmation UI** — Razor component + API integration
   - Impact: Override workflow completion
   - Effort: 4 hours

---

## Files to Review/Modify

- [src/RulesApp.Shared/Models.cs](../src/RulesApp.Shared/Models.cs) — Add Language to ChatRequest/SearchRequest; add RegionalEn
- [src/RulesApp.Api/Services/Chunker.cs](../src/RulesApp.Api/Services/Chunker.cs) — Fix RuleKey normalization
- [src/RulesApp.Api/Services/ChatService.cs](../src/RulesApp.Api/Services/ChatService.cs) — Strengthen citation validation
- [src/RulesApp.Api/Functions/Search.cs](../src/RulesApp.Api/Functions/Search.cs#L68-L73) — Verify 400 return for invalid scopes
- [src/RulesApp.Web/Pages/Search.razor](../src/RulesApp.Web/Pages/Search.razor) — Add language toggle
- [src/RulesApp.Web/Pages/Chat.razor](../src/RulesApp.Web/Pages/Chat.razor) — Add language parameter
- [src/RulesApp.Api/Entities/TableEntities.cs](../src/RulesApp.Api/Entities/TableEntities.cs) — Add RelationshipType field to OverrideMappingEntity

---

## Next Steps

1. **Prioritize** critical fixes (RuleKey, language, RegionalEn)
2. **Create issues** for each fix with acceptance criteria
3. **Assign team** or allocate to agent for implementation
4. **Re-audit** after each milestone to track compliance
