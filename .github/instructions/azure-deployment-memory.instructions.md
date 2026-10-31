---
description: 'Workspace deployment memory for Azure commands and environment-specific defaults.'
applyTo: '**/*.bicep,**/*.bicepparam,**/infrastructure/**,**/.github/workflows/*.yml'
---

# Azure Deployment Memory

Keep deployment commands aligned to the environment naming used by this repository.

## Production resource group default

Use `nerr-bcwp-rg-prod-sc` as the production resource group in deployment examples and first-run commands unless the user explicitly provides a different target.
