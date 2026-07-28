# Azure Deployment Runbook

This is the complete one-time setup guide to deploy Rora Quest to Azure Container Apps.

---

## Prerequisites
- Azure CLI installed and logged in (`az login`)
- Docker Desktop running locally
- An Azure subscription
- Your GitHub repo URL (you'll push the code once below)

---

## Step 1 — Push code to GitHub (once)

```bash
# If you haven't created a GitHub repo yet, create one at https://github.com/new
# Then:
cd C:\Personal\workspace
git remote add origin https://github.com/<YOUR_USERNAME>/rora-quest.git
git push -u origin master
```

GitHub Actions will trigger automatically on every future push to `master`.

---

## Step 2 — Create Azure resources

```bash
# Variables — set these once
RESOURCE_GROUP="Rora-Quest"
LOCATION="canadacentral"
ACR_NAME="roraquestacr"          # must be globally unique, lowercase, alphanumeric only

# Create resource group
az group create --name $RESOURCE_GROUP --location $LOCATION

# Create Azure Container Registry
az acr create \
  --resource-group $RESOURCE_GROUP \
  --name $ACR_NAME \
  --sku Basic \
  --admin-enabled false

# Deploy Container Apps Environment + Key Vault + Container Apps via Bicep
# First, update infra/aca/main.parameters.json with your ACR name
az deployment group create \
  --resource-group $RESOURCE_GROUP \
  --template-file rora-quest/infra/aca/main.bicep \
  --parameters @rora-quest/infra/aca/main.parameters.json
```

After deployment, the outputs will show:
- `apiUrl` — your production API URL
- `webUrl` — your production web URL
- `keyVaultName` — Key Vault name to load secrets into

---

## Step 3 — Load secrets into Key Vault

```bash
KEY_VAULT_NAME="rora-quest-kv-prod"  # from deployment output

# PostgreSQL connection string
az keyvault secret set \
  --vault-name $KEY_VAULT_NAME \
  --name "PostgresConnectionString" \
  --value "Host=roraqueststore.postgres.database.azure.com;Port=5432;Database=rora-quest-db;Username=cgbimbu;Password=<YOUR_PG_PASSWORD>;SSL Mode=Require;Trust Server Certificate=false;"

# Entra client secret (the one you set locally as EntraAuth__ClientSecret)
az keyvault secret set \
  --vault-name $KEY_VAULT_NAME \
  --name "EntraClientSecret" \
  --value "<palceholder>"
```

**Important:** After this step, verify the API container app can read secrets:
```bash
az containerapp show -n rora-quest-api -g $RESOURCE_GROUP --query "properties.provisioningState"
# Should output: "Succeeded"
```

---

## Step 4 — Add GitHub Actions secrets

In your GitHub repo → Settings → Secrets and Variables → Actions, add:

| Secret name | Value |
|---|---|
| `ACR_LOGIN_SERVER` | `<ACR_NAME>.azurecr.io` |
| `ACR_USERNAME` | output of `az acr credential show -n <ACR_NAME> --query username -o tsv` |
| `ACR_PASSWORD` | output of `az acr credential show -n <ACR_NAME> --query passwords[0].value -o tsv` |
| `AZURE_CREDENTIALS` | output of the `az ad sp create-for-rbac` command below |

```bash
# Create a service principal for GitHub Actions to deploy
az ad sp create-for-rbac \
  --name "rora-quest-github-actions" \
  --role "Contributor" \
  --scopes "/subscriptions/<SUBSCRIPTION_ID>/resourceGroups/$RESOURCE_GROUP" \
  --sdk-auth
# Copy the JSON output and paste it as the AZURE_CREDENTIALS secret
```

Also add these as GitHub **Variables** (not secrets):

| Variable name | Value |
|---|---|
| `AZURE_RESOURCE_GROUP` | `Rora-Quest` |
| `ACR_NAME` | `roraquestacr` |

---

## Step 5 — Add production redirect URI to Entra app

1. Go to: https://portal.azure.com/#blade/Microsoft_AAD_RegisteredApps/ApplicationMenuBlade/Authentication/appId/6d46b91d-8f72-4ac0-ac88-03c044f86ea9
2. Click **Add a platform** → **Web**
3. Add redirect URI: `https://<apiUrl>/signin-oidc`
   - Replace `<apiUrl>` with the `apiUrl` output from Step 2
4. Click **Save**

---

## Step 6 — Trigger first deploy

Push any change (or re-push `master`) to trigger GitHub Actions:

```bash
git push origin master
```

Watch the Actions tab: `Build and Push Images` completes first, then `Deploy to Azure Container Apps` runs.

---

## Step 7 — Smoke test

```bash
# Get URLs
az containerapp show -n rora-quest-api -g $RESOURCE_GROUP \
  --query "properties.configuration.ingress.fqdn" -o tsv

az containerapp show -n rora-quest-web -g $RESOURCE_GROUP \
  --query "properties.configuration.ingress.fqdn" -o tsv

# Verify API health
curl -s https://<API_FQDN>/health
# Expected: {"status":"healthy","database":"connected"}

# Open web URL in browser, sign in with Microsoft
```

---

## Useful commands after setup

```bash
# View API logs
az containerapp logs show -n rora-quest-api -g $RESOURCE_GROUP --tail 50

# Restart API (pick up config changes)
az containerapp revision restart \
  -n rora-quest-api \
  -g $RESOURCE_GROUP \
  --revision <revision-name>

# Update to a specific image tag
az containerapp update \
  -n rora-quest-api \
  -g $RESOURCE_GROUP \
  --image <ACR_NAME>.azurecr.io/rora-quest-api:<tag>
```
