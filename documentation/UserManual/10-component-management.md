---
title: Component management
category: Engineering
order: 10
version: 0.4.0
keywords: device, component, coil, contact, cross reference, properties, catalog
---
# Component management

A **device** is one physical component with a tag. It can be represented by several symbols: a contactor has a coil (main function) and contacts (child contacts) on different sheets. Give contacts the same tag as the coil to associate them.

## Cross-references

- Contacts show the location of their coil under the tag (e.g. `/4.2` = sheet 4, column 2).
- Coils show a **contact mirror** listing each contact with its pins and location.

## Properties

Select a symbol to edit: tag, function text, location, manufacturer, part number and engineering properties (rating, voltage, settings). Pin numbers of contacts (e.g. 13-14 → 43-44) can be overridden per symbol.

## Catalogue

Choose a part number from the catalogue in the **Part number** field: the manufacturer is filled in and the product dimensions are used by the cabinet layout; prices feed the BOM. Products marked *demo* are fictional.
