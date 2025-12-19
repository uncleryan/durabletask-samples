-- Copyright (c) Microsoft Corporation.
-- Licensed under the MIT License.

-- Drop all schema objects for cleanup
-- Note: $(SchemaName) will be replaced at runtime with the actual schema name

-- Drop all schema objects in reverse dependency order
DROP SCHEMA IF EXISTS $(SchemaName) CASCADE;
