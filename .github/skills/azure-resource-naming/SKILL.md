---
name: azure-resource-naming
description: 'Generates consistent Azure resource names using Nick Rogoff naming pattern with Microsoft CAF resource abbreviations first. Use when asked to name Azure resources, define naming conventions, create Bicep/Terraform naming patterns, standardize environment-region naming, choose Azure short codes, or suggest abbreviations for resource types not listed in CAF.'
license: Complete terms in LICENSE.txt
---

# Azure Resource Naming Skill

Generate Azure resource names using a consistent pattern:

- Naming structure and principles from Nick Rogoff's convention.
- Resource type abbreviations primarily from Microsoft CAF abbreviation guidance.
- Region code seed list defined in this skill.

## When to Use This Skill

- User asks to name one or more Azure resources.
- User asks for a naming convention for Azure resources.
- User needs resource type short codes for Bicep, ARM, or Terraform.
- User asks for environment and region short-code naming.
- User asks for abbreviation suggestions for resource types that are not in CAF.

## Naming Pattern

Primary (hyphenated) pattern:

`{org}-{project}-{rtype}-{app}-{env}-{region}[-{instance}]`

Compact variant for resources that do not allow hyphens (for example, storage account names):

`{org}{project}{rtype}{app}{env}{region}[{instance}]`

### Elements

- `org`: organization short code (3-8 chars recommended)
- `project`: project/platform short code
- `rtype`: resource type abbreviation (CAF-first)
- `app`: workload/app/purpose short code
- `env`: environment short code
- `region`: region short code
- `instance`: optional instance number (01, 02, ...)

## Fixed Short Code Tables

### Environment codes

- Development: `dev`
- QA/Test: `qa`
- System Integration Test: `sit`
- User Acceptance Test: `uat`
- Staging: `stag`
- Pre-production: `pre`
- Production: `prod`

### Region codes (initial set)

- North Europe: `ne`
- UK South: `uks`
- West Europe: `we`
- Norway East: `nore`
- East US: `eus`

## Resource Type Abbreviation Rules

1. First choice: use the Microsoft CAF abbreviation for the specific Azure resource type and provider namespace.
2. If CAF has multiple options for a provider, pick the one that matches the specific resource kind/path.
3. If CAF uses a descriptive placeholder (for example DNS), use a descriptive code that reflects the actual domain/purpose.
4. If no CAF abbreviation exists, suggest one using the fallback method below.

### Fallback abbreviation method (when CAF has no match)

Build a proposed short code with these rules:

1. Start from the resource type name (not the full namespace), lowercase.
2. Remove generic filler words first where safe: `azure`, `service`, `services`, `resource`, `account`.
3. Split into tokens by case/space/separators.
4. Create a 2-5 character candidate:
   - Single token: keep first consonant-heavy 3-4 chars.
   - Multi-token: use initials first; if too short, append consonants from the primary token.
5. Avoid collision with common existing abbreviations in the same naming scope.
6. If collision exists, add one more meaningful character from the primary token.
7. Return the suggestion explicitly as `proposed`, not `official`.

Examples:

- `WorkloadIdentities` -> `wid`
- `ChaosExperiments` -> `chx`
- `MediaServicesLiveEvents` -> `msle`

## Naming Guardrails

- Default to lowercase names unless a resource explicitly supports and the team requires PascalCase.
- Prefer hyphens for readability where platform rules allow.
- Keep names concise and URL-safe.
- Do not start or end names with special characters.
- If environment is omitted, assume `prod` only when user confirms this policy.
- For globally unique resources, tune `project` or `app` code before adding random suffixes.

## Resource-Specific Constraint Handling

Always validate against Azure naming restrictions for each resource type.

Minimum built-in handling in this skill:

- Storage accounts:
  - Use compact format (no hyphens).
  - 3-24 chars.
  - lowercase letters and numbers only.

If a generated name violates constraints:

1. Shorten `app` first.
2. Shorten `project` second.
3. Trim optional `instance` formatting only if still unique and policy allows.
4. Re-check constraints and uniqueness intent.

## Step-by-Step Workflow

1. Collect required inputs:
   - organization code, project code, app code
   - environment, region
   - target resource type(s)
   - optional instance number
2. Resolve environment and region from this skill tables.
3. Resolve resource type abbreviation using CAF-first lookup.
4. If not found, create a fallback abbreviation and label it `proposed`.
5. Generate both forms when relevant:
   - standard hyphenated name
   - compact constrained variant (if resource requires it)
6. Validate for length/character rules for each target resource.
7. Return names in a table with columns:
   - resource type
   - abbreviation source (`caf` or `proposed`)
   - final name
   - notes/constraints

## Output Format

Use this template when returning names:

| Resource Type | Provider Namespace | Abbreviation | Source | Name | Notes |
|---|---|---|---|---|---|
| Storage Account | Microsoft.Storage/storageAccounts | st | caf | nerrteststdiagdevne | compact, 24-char check |

## Gotchas

- CAF abbreviations are recommendations, not hard enforcement rules. Keep org-level consistency once chosen.
- A single provider can map to different abbreviations based on resource path or kind (for example `Microsoft.Web/sites` can represent Function App or Web App scenarios).
- Storage accounts and a few other global-name resources often break the readable hyphenated pattern; always produce a compliant compact variant.
- Do not mark fallback abbreviations as official. They are proposed conventions pending team approval.

## References

- Nick Rogoff convention: https://nicholasrogoff.com/2019/11/13/cloud-resource-naming-convention-azure/
- Microsoft CAF abbreviations: https://learn.microsoft.com/en-us/azure/cloud-adoption-framework/ready/azure-best-practices/resource-abbreviations
- Azure resource naming rules and restrictions: https://learn.microsoft.com/en-us/azure/azure-resource-manager/management/resource-name-rules
