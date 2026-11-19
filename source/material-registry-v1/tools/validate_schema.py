#!/usr/bin/env python3
"""Validate material-registry.json against registry-schema.json (Draft 2020-12). Exit 1 on any violation."""
import json, sys
from jsonschema import Draft202012Validator
schema = json.load(open(sys.argv[2], encoding="utf-8")); doc = json.load(open(sys.argv[1], encoding="utf-8"))
Draft202012Validator.check_schema(schema)
errors = sorted(Draft202012Validator(schema).iter_errors(doc), key=lambda e: list(e.path))
for e in errors[:50]: print("SCHEMA ERROR", "/".join(map(str, e.path)), e.message[:200])
print(f"schema: {len(errors)} error(s), {len(doc['entries'])} entries, {len(doc['tags'])} tags")
sys.exit(1 if errors else 0)
