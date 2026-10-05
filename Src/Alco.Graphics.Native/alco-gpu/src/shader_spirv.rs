//! Conservative normalization of Slang's default-only switch wrappers before Naga.
//!
//! Naga 30's SPIR-V frontend loses the target of an enclosing-loop break inside
//! a switch. Removing a redundant wrapper before parsing preserves that target.

use std::collections::{HashMap, HashSet};

const MAGIC: u32 = 0x0723_0203;
const TYPE_INT: u16 = 21;
const FUNCTION: u16 = 54;
const FUNCTION_END: u16 = 56;
const LOOP_MERGE: u16 = 246;
const SELECTION_MERGE: u16 = 247;
const LABEL: u16 = 248;
const BRANCH: u16 = 249;
const BRANCH_CONDITIONAL: u16 = 250;
const SWITCH: u16 = 251;

#[derive(Clone, Copy)]
struct Instruction {
    offset: usize,
    count: usize,
    op: u16,
}

#[derive(Clone, Copy)]
struct Construct {
    merge: usize,
    continuing: Option<usize>,
}

struct Block {
    label: u32,
    successors: Vec<u32>,
    predecessors: Vec<usize>,
    construct: Option<Construct>,
    terminator: u16,
    terminator_offset: usize,
    label_offset: usize,
    merge_offset: Option<usize>,
    has_phi: bool,
    branch_only: bool,
    candidate: Option<(usize, usize, u32)>,
}

#[derive(Default)]
struct Rewrites {
    wrappers: Vec<(usize, usize, u32)>,
    operands: Vec<(usize, u32)>,
    bridges: Vec<(usize, Vec<(u32, u32)>)>,
}

fn instruction(words: &[u32], offset: usize) -> Result<Instruction, &'static str> {
    let word = *words.get(offset).ok_or("truncated SPIR-V instruction")?;
    let count = (word >> 16) as usize;
    if count == 0 || count > words.len() - offset {
        return Err("invalid SPIR-V instruction word count");
    }
    Ok(Instruction {
        offset,
        count,
        op: word as u16,
    })
}

fn contains(bits: &[u64], index: usize) -> bool {
    bits[index / 64] & (1 << (index % 64)) != 0
}

fn dominators(blocks: &[Block], indices: &HashMap<u32, usize>) -> Vec<Vec<u64>> {
    let mut reachable = vec![false; blocks.len()];
    let mut pending = vec![0];
    while let Some(index) = pending.pop() {
        if std::mem::replace(&mut reachable[index], true) {
            continue;
        }
        pending.extend(blocks[index].successors.iter().map(|id| indices[id]));
    }
    let mut all = vec![0u64; blocks.len().div_ceil(64)];
    for (index, &is_reachable) in reachable.iter().enumerate() {
        if is_reachable {
            all[index / 64] |= 1 << (index % 64);
        }
    }
    let mut result = vec![all.clone(); blocks.len()];
    for index in 0..blocks.len() {
        if !reachable[index] || index == 0 {
            result[index].fill(0);
            if reachable[index] {
                result[index][index / 64] |= 1 << (index % 64);
            }
        }
    }
    loop {
        let mut changed = false;
        for index in 1..blocks.len() {
            if !reachable[index] {
                continue;
            }
            let mut next = all.clone();
            for &predecessor in &blocks[index].predecessors {
                if reachable[predecessor] {
                    for (word, &predecessor_word) in next.iter_mut().zip(&result[predecessor]) {
                        *word &= predecessor_word;
                    }
                }
            }
            next[index / 64] |= 1 << (index % 64);
            if next != result[index] {
                result[index] = next;
                changed = true;
            }
        }
        if !changed {
            return result;
        }
    }
}

fn eligible(
    header: usize,
    default: usize,
    blocks: &[Block],
    indices: &HashMap<u32, usize>,
    dom: &[Vec<u64>],
    allow_tail_selections: bool,
) -> Option<Vec<usize>> {
    let merge = blocks[header].construct.unwrap().merge;
    let mut expanded = Vec::new();
    if default == header
        || default == merge
        || !contains(&dom[default], header)
        || !contains(&dom[merge], header)
        || blocks[merge].predecessors.is_empty()
    {
        return None;
    }

    let mut boundaries = HashSet::new();
    let mut in_loop = false;
    for (index, block) in blocks.iter().enumerate() {
        let Some(construct) = block.construct else {
            continue;
        };
        // Shared merge labels or headers at another construct's boundary need
        // more extensive restructuring than this deliberately narrow pass.
        if index != header
            && (construct.merge == merge
                || construct.merge == header
                || construct.continuing == Some(header)
                || construct.continuing == Some(merge))
        {
            return None;
        }
        if index != header
            && contains(&dom[header], index)
            && !contains(&dom[header], construct.merge)
        {
            boundaries.insert(construct.merge);
            if let Some(continuing) = construct.continuing {
                in_loop = true;
                boundaries.insert(continuing);
                boundaries.insert(index);
            }
        }
    }
    if !in_loop || blocks[merge].construct.is_some() {
        return None;
    }

    // Find the wrapper's region, stopping at its own merge and at legal exits
    // to ancestor constructs. In particular, do not follow an outer loop's
    // backedge and accidentally include the next execution of this wrapper.
    let mut region = vec![false; blocks.len()];
    let mut pending = vec![default];
    while let Some(index) = pending.pop() {
        if index == merge || boundaries.contains(&index) {
            continue;
        }
        if index == header || !contains(&dom[index], header) {
            return None;
        }
        if std::mem::replace(&mut region[index], true) {
            continue;
        }
        pending.extend(blocks[index].successors.iter().map(|id| indices[id]));
    }

    for &predecessor in &blocks[merge].predecessors {
        if !region[predecessor]
            || blocks[predecessor].terminator != BRANCH
            || blocks[predecessor].successors != [blocks[merge].label]
        {
            return None;
        }
        // Without the wrapper, an early exit from a nested selection to the
        // wrapper's merge would no longer be a legal structured exit. Require
        // every nested construct that dominates this edge to have finished.
        for (nested, block) in blocks.iter().enumerate() {
            let Some(construct) = block.construct else {
                continue;
            };
            if !region[nested]
                || !contains(&dom[predecessor], nested)
                || contains(&dom[predecessor], construct.merge)
            {
                continue;
            }
            // A break from an enclosing nested loop can legally bypass the
            // merges of that loop's child selections/switches. Its merge must
            // already dominate this predecessor; do not exempt an early exit
            // from a child selection that is still inside the loop.
            let exited_loop = blocks.iter().enumerate().any(|(loop_header, loop_block)| {
                let Some(loop_construct) = loop_block.construct else {
                    return false;
                };
                region[loop_header]
                    && loop_construct.continuing.is_some()
                    && contains(&dom[nested], loop_header)
                    && !contains(&dom[nested], loop_construct.merge)
                    && contains(&dom[predecessor], loop_construct.merge)
            });
            if !exited_loop {
                if !allow_tail_selections
                    || blocks[merge].has_phi
                    || construct.continuing.is_some()
                    || block.terminator != BRANCH_CONDITIONAL
                    || !region[construct.merge]
                    || blocks[construct.merge].has_phi
                    || !contains(&dom[construct.merge], nested)
                {
                    return None;
                }
                if !expanded.contains(&nested) {
                    expanded.push(nested);
                }
            }
        }
    }
    // Expanded tail selections must form a strict dominance chain. A source
    // continue can skip the rest of that chain, but unrelated branches cannot
    // be combined into the same sequence of fresh merge blocks.
    expanded.sort_unstable_by_key(|&nested| {
        dom[nested]
            .iter()
            .map(|word| word.count_ones())
            .sum::<u32>()
    });
    for pair in expanded.windows(2) {
        if !contains(&dom[pair[1]], pair[0]) {
            return None;
        }
    }
    for &nested in &expanded {
        let old_merge = blocks[nested].construct.unwrap().merge;
        for (index, block) in blocks.iter().enumerate() {
            if index != nested
                && block.construct.is_some_and(|construct| {
                    construct.merge == old_merge || construct.continuing == Some(old_merge)
                })
            {
                return None;
            }
            // After expansion, every reachable tail block must still be
            // dominated by this selection header. Reject cross-entry tails.
            if region[index] && contains(&dom[index], old_merge) && !contains(&dom[index], nested) {
                return None;
            }
        }
    }
    Some(expanded)
}

fn tail_destinations(
    header: usize,
    expanded: &[usize],
    blocks: &[Block],
    indices: &HashMap<u32, usize>,
    dom: &[Vec<u64>],
) -> Option<Vec<usize>> {
    let wrapper_merge = blocks[header].construct.unwrap().merge;
    let mut destinations = Vec::new();
    for &nested in expanded {
        let mut destination = wrapper_merge;
        let mut depth = 0;
        for (ancestor, block) in blocks.iter().enumerate() {
            let Some(construct) = block.construct else {
                continue;
            };
            if ancestor == header
                || ancestor == nested
                || expanded.contains(&ancestor)
                || !contains(&dom[ancestor], header)
                || !contains(&dom[nested], ancestor)
                || contains(&dom[nested], construct.merge)
            {
                continue;
            }
            // Do not expand through a genuine switch or a loop boundary.
            if construct.continuing.is_some() || block.terminator != BRANCH_CONDITIONAL {
                return None;
            }
            let ancestor_depth = dom[ancestor].iter().map(|word| word.count_ones()).sum();
            if ancestor_depth > depth {
                depth = ancestor_depth;
                destination = construct.merge;
            }
        }
        if destination != wrapper_merge {
            let mut cursor = destination;
            let mut visited = HashSet::new();
            while cursor != wrapper_merge {
                if !visited.insert(cursor)
                    || !blocks[cursor].branch_only
                    || blocks[cursor].construct.is_some()
                    || blocks[cursor].has_phi
                    || blocks[cursor].successors.len() != 1
                {
                    return None;
                }
                cursor = indices[&blocks[cursor].successors[0]];
            }
            for &predecessor in &blocks[destination].predecessors {
                if contains(&dom[predecessor], nested) && blocks[predecessor].terminator != BRANCH {
                    return None;
                }
            }
        }
        // Moving this selection's merge can invalidate a child's formerly
        // legal early exit to that old merge. Decline such cross-construct
        // entries rather than attempting recursive restructuring.
        let old_merge = blocks[nested].construct.unwrap().merge;
        for &predecessor in &blocks[old_merge].predecessors {
            for (child, child_block) in blocks.iter().enumerate() {
                let Some(child_construct) = child_block.construct else {
                    continue;
                };
                if child != nested
                    && contains(&dom[child], nested)
                    && contains(&dom[predecessor], child)
                    && !contains(&dom[predecessor], child_construct.merge)
                {
                    return None;
                }
            }
        }
        destinations.push(destination);
    }
    // Fresh merge chains may share one tail destination. Different ancestor
    // boundaries would require a more general region-nesting transformation.
    if destinations.windows(2).any(|pair| pair[0] != pair[1]) {
        return None;
    }
    Some(destinations)
}

fn has_enclosing_loop_break(
    header: usize,
    blocks: &[Block],
    indices: &HashMap<u32, usize>,
    dom: &[Vec<u64>],
) -> bool {
    let merge = blocks[header].construct.unwrap().merge;
    let mut stops = HashSet::from([header, merge]);
    let mut loop_merges = HashSet::new();
    for (ancestor, block) in blocks.iter().enumerate() {
        let Some(construct) = block.construct else {
            continue;
        };
        if ancestor != header
            && contains(&dom[header], ancestor)
            && !contains(&dom[header], construct.merge)
        {
            stops.insert(construct.merge);
            if let Some(continuing) = construct.continuing {
                loop_merges.insert(construct.merge);
                stops.insert(continuing);
                stops.insert(ancestor);
            }
        }
    }
    let mut visited = HashSet::new();
    let mut pending = vec![indices[&blocks[header].successors[0]]];
    while let Some(index) = pending.pop() {
        if loop_merges.contains(&index) {
            return true;
        }
        if stops.contains(&index) || !visited.insert(index) {
            continue;
        }
        pending.extend(blocks[index].successors.iter().map(|id| indices[id]));
    }
    false
}

fn function_rewrites(
    words: &[u32],
    instructions: &[Instruction],
    integer_widths: &HashMap<u32, u32>,
    allow_tail_selections: bool,
    check_hazards: bool,
    next_id: &mut u32,
) -> Result<Rewrites, &'static str> {
    let mut blocks: Vec<Block> = Vec::new();
    let mut indices = HashMap::new();
    let mut merges: Vec<Option<(u32, Option<u32>)>> = Vec::new();
    for (position, &inst) in instructions.iter().enumerate() {
        let args = &words[inst.offset + 1..inst.offset + inst.count];
        if inst.op == LABEL {
            if inst.count != 2 || indices.insert(args[0], blocks.len()).is_some() {
                return Err("invalid or duplicate SPIR-V block label");
            }
            if blocks.last().is_some_and(|block| block.terminator == 0) {
                return Err("SPIR-V block has no terminator");
            }
            blocks.push(Block {
                label: args[0],
                successors: Vec::new(),
                predecessors: Vec::new(),
                construct: None,
                terminator: 0,
                terminator_offset: 0,
                label_offset: inst.offset,
                merge_offset: None,
                has_phi: false,
                branch_only: true,
                candidate: None,
            });
            merges.push(None);
            continue;
        }
        let Some(block) = blocks.last_mut() else {
            continue;
        };
        if !matches!(inst.op, BRANCH | 0 | 8 | 317) {
            block.branch_only = false;
        }
        if inst.op == 245 {
            block.has_phi = true;
        }
        if matches!(inst.op, LOOP_MERGE | SELECTION_MERGE) {
            block.merge_offset = Some(inst.offset);
            if inst.count < if inst.op == LOOP_MERGE { 4 } else { 3 }
                || merges.last().unwrap().is_some()
            {
                return Err("invalid SPIR-V merge instruction");
            }
            *merges.last_mut().unwrap() = Some((args[0], (inst.op == LOOP_MERGE).then(|| args[1])));
        }
        match inst.op {
            BRANCH if inst.count == 2 => block.successors.push(args[0]),
            BRANCH_CONDITIONAL if inst.count >= 4 => {
                block.successors.extend_from_slice(&args[1..3]);
            }
            SWITCH if inst.count >= 3 => {
                block.successors.push(args[1]);
                if inst.count == 3 {
                    if let Some(previous) = position.checked_sub(1).map(|i| instructions[i]) {
                        if previous.op == SELECTION_MERGE && previous.count == 3 {
                            block.candidate = Some((previous.offset, inst.offset, args[1]));
                        }
                    }
                } else {
                    let width = integer_widths
                        .get(&args[0])
                        .ok_or("unknown SPIR-V switch selector width")?;
                    let stride = if *width == 64 { 3 } else { 2 };
                    if !(inst.count - 3).is_multiple_of(stride) {
                        return Err("invalid SPIR-V switch cases");
                    }
                    for case in args[2..].chunks_exact(stride) {
                        block.successors.push(case[stride - 1]);
                    }
                }
            }
            252 | 253 | 254 | 255 | 4416 | 4448 | 4449 | 5294 => {}
            _ => continue,
        }
        if block.terminator != 0 {
            return Err("multiple SPIR-V block terminators");
        }
        block.terminator = inst.op;
        block.terminator_offset = inst.offset;
    }
    if blocks.is_empty() || blocks.last().unwrap().terminator == 0 {
        return Err("SPIR-V function has no terminated block");
    }
    for index in 0..blocks.len() {
        if let Some((merge, continuing)) = merges[index] {
            blocks[index].construct = Some(Construct {
                merge: *indices.get(&merge).ok_or("missing SPIR-V merge block")?,
                continuing: continuing
                    .map(|id| {
                        indices
                            .get(&id)
                            .copied()
                            .ok_or("missing SPIR-V continue block")
                    })
                    .transpose()?,
            });
        }
        for successor in blocks[index].successors.clone() {
            let successor_index = *indices
                .get(&successor)
                .ok_or("missing SPIR-V branch target")?;
            blocks[successor_index].predecessors.push(index);
        }
    }
    let dom = dominators(&blocks, &indices);
    let mut rewrites = Rewrites::default();
    // Avoid overlapping expansions in one pass. The caller reparses after each
    // change, so a removed inner wrapper can expose a safe outer wrapper.
    let mut claimed = HashSet::new();
    for (header, block) in blocks.iter().enumerate() {
        let Some((merge_offset, switch_offset, default)) = block.candidate else {
            continue;
        };
        if block.construct.is_none() {
            continue;
        }
        if check_hazards {
            if has_enclosing_loop_break(header, &blocks, &indices, &dom) {
                return Err("unsupported SPIR-V loop break through default-only switch; Naga would lose its target");
            }
            continue;
        }
        let Some(expanded) = eligible(
            header,
            indices[&default],
            &blocks,
            &indices,
            &dom,
            allow_tail_selections,
        ) else {
            continue;
        };
        if expanded.iter().any(|nested| claimed.contains(nested)) {
            continue;
        }
        let merge = block.construct.unwrap().merge;
        let mut expanded = expanded;
        let all_expanded = expanded.clone();
        expanded.retain(|&ancestor| {
            let ancestor_merge = blocks[ancestor].construct.unwrap().merge;
            let mut cursor = ancestor_merge;
            let mut visited = HashSet::new();
            while cursor != merge {
                if !visited.insert(cursor)
                    || !blocks[cursor].branch_only
                    || blocks[cursor].construct.is_some()
                    || blocks[cursor].successors.len() != 1
                {
                    return true;
                }
                cursor = indices[&blocks[cursor].successors[0]];
            }
            // A descendant's fresh merge before this silent ancestor tail
            // already turns all of the ancestor's early exits into legal exits.
            !blocks[merge].predecessors.iter().all(|&predecessor| {
                !contains(&dom[predecessor], ancestor)
                    || contains(&dom[predecessor], ancestor_merge)
                    || all_expanded.iter().any(|&child| {
                        child != ancestor
                            && contains(&dom[child], ancestor)
                            && contains(&dom[predecessor], child)
                    })
            })
        });
        // An inner expansion must never move a retained ancestor merge with
        // work. Only the proven branch-only ancestor-tail case is supported.
        if expanded.iter().any(|&ancestor| {
            expanded.iter().any(|&child| {
                child != ancestor
                    && contains(&dom[child], ancestor)
                    && !contains(&dom[child], blocks[ancestor].construct.unwrap().merge)
            }) && !blocks[blocks[ancestor].construct.unwrap().merge].branch_only
        }) {
            continue;
        }
        let Some(destinations) = tail_destinations(header, &expanded, &blocks, &indices, &dom)
        else {
            continue;
        };
        if !expanded.is_empty() && blocks.iter().any(|block| block.label >= words[3]) {
            return Err("SPIR-V ID bound does not include all block labels");
        }
        let mut new_merges = Vec::new();
        for &nested in &expanded {
            let id = *next_id;
            *next_id = next_id.checked_add(1).ok_or("SPIR-V ID bound overflow")?;
            new_merges.push(id);
            rewrites
                .operands
                .push((blocks[nested].merge_offset.unwrap() + 1, id));
            claimed.insert(nested);
        }
        if !expanded.is_empty() {
            let destination = destinations[0];
            let mut silent_tail = HashSet::new();
            let mut cursor = destination;
            while cursor != merge {
                silent_tail.insert(cursor);
                cursor = indices[&blocks[cursor].successors[0]];
            }
            let mut predecessors = blocks[merge].predecessors.clone();
            if destination != merge {
                predecessors.extend_from_slice(&blocks[destination].predecessors);
            }
            for &predecessor in &predecessors {
                if silent_tail.contains(&predecessor) {
                    continue;
                }
                if let Some(position) = expanded
                    .iter()
                    .rposition(|&nested| contains(&dom[predecessor], nested))
                {
                    rewrites.operands.push((
                        blocks[predecessor].terminator_offset + 1,
                        new_merges[position],
                    ));
                }
            }
            let bridges = (0..expanded.len())
                .rev()
                .map(|position| {
                    let target = if position == 0 {
                        blocks[destination].label
                    } else {
                        new_merges[position - 1]
                    };
                    (new_merges[position], target)
                })
                .collect();
            rewrites
                .bridges
                .push((blocks[destination].label_offset, bridges));
        }
        rewrites
            .wrappers
            .push((merge_offset, switch_offset, default));
    }
    Ok(rewrites)
}

/// Removes redundant default-only switch wrappers inside loops.
///
/// Source continue paths can require expanding a dominance chain of selections
/// through the loop-body tail with fresh, unique merge labels. Existing values
/// and phi predecessors remain unchanged; phi-bearing tail merges and ambiguous
/// shapes without nonlocal loop breaks are preserved. Unsupported nonlocal loop
/// breaks fail before Naga can miscompile them. Valid SPIR-V IDs and their header
/// bound are prerequisites; this pass is not a complete module validator. Errors
/// never partially modify `words`.
pub(crate) fn normalize(words: &mut Vec<u32>) -> Result<usize, &'static str> {
    let mut output = words.clone();
    let mut total = 0;
    // First erase wrappers without restructuring. Later passes can then
    // expand a remaining wrapper without overlapping any nested switch.
    for allow_tail_selections in [false, true] {
        loop {
            let count = normalize_pass(&mut output, allow_tail_selections, false)?;
            if count == 0 {
                break;
            }
            total += count;
        }
    }
    normalize_pass(&mut output, true, true)?;
    if total != 0 {
        *words = output;
    }
    Ok(total)
}

fn normalize_pass(
    words: &mut Vec<u32>,
    allow_tail_selections: bool,
    check_hazards: bool,
) -> Result<usize, &'static str> {
    if words.len() < 5 || words[0] != MAGIC {
        return Err("invalid SPIR-V header");
    }
    let mut offset = 5;
    let mut previous: Option<Instruction> = None;
    let mut has_candidate = false;
    while offset < words.len() {
        let inst = instruction(words, offset)?;
        has_candidate |= inst.op == SWITCH
            && inst.count == 3
            && previous.is_some_and(|p| p.op == SELECTION_MERGE && p.count == 3);
        offset += inst.count;
        previous = Some(inst);
    }
    if !has_candidate {
        return Ok(0);
    }

    let mut instructions = Vec::new();
    offset = 5;
    while offset < words.len() {
        let inst = instruction(words, offset)?;
        offset += inst.count;
        instructions.push(inst);
    }
    let mut integer_types = HashMap::new();
    for inst in &instructions {
        if inst.op == TYPE_INT && inst.count == 4 {
            integer_types.insert(words[inst.offset + 1], words[inst.offset + 2]);
        }
    }
    let mut integer_widths = HashMap::new();
    let mut function_start = None;
    let mut functions = Vec::new();
    for (index, inst) in instructions.iter().enumerate() {
        if inst.op == FUNCTION && (inst.count != 5 || function_start.replace(index).is_some()) {
            return Err("invalid SPIR-V function start");
        }
        // In valid SPIR-V, an integer type ID in the first operand of a
        // function instruction identifies a typed result. Global constants
        // are the other possible integer switch selectors.
        if inst.count >= 3
            && (function_start.is_some() || inst.op == 1 || (41..=52).contains(&inst.op))
        {
            if let Some(&width) = integer_types.get(&words[inst.offset + 1]) {
                integer_widths.insert(words[inst.offset + 2], width);
            }
        }
        if inst.op == FUNCTION_END {
            let start = function_start
                .take()
                .ok_or("unexpected SPIR-V function end")?;
            functions.push((start + 1, index));
        }
    }
    if function_start.is_some() {
        return Err("unterminated SPIR-V function");
    }
    let mut rewrites = Rewrites::default();
    let mut next_id = words[3];
    for (start, end) in functions {
        let body = &instructions[start..end];
        if body.windows(2).any(|pair| {
            pair[0].op == SELECTION_MERGE
                && pair[0].count == 3
                && pair[1].op == SWITCH
                && pair[1].count == 3
        }) {
            let function = function_rewrites(
                words,
                body,
                &integer_widths,
                allow_tail_selections,
                check_hazards,
                &mut next_id,
            )?;
            rewrites.wrappers.extend(function.wrappers);
            rewrites.operands.extend(function.operands);
            rewrites.bridges.extend(function.bridges);
        }
    }
    if rewrites.wrappers.is_empty() {
        return Ok(0);
    }
    rewrites.wrappers.sort_unstable_by_key(|rewrite| rewrite.0);
    let operands: HashMap<_, _> = rewrites.operands.into_iter().collect();
    let bridges: HashMap<_, _> = rewrites.bridges.into_iter().collect();
    let mut output = words[..5].to_vec();
    output[3] = next_id;
    let mut wrapper_index = 0;
    let mut offset = 5;
    while offset < words.len() {
        if let Some(inserted) = bridges.get(&offset) {
            for &(label, target) in inserted {
                output.extend_from_slice(&[
                    (2 << 16) | LABEL as u32,
                    label,
                    (2 << 16) | BRANCH as u32,
                    target,
                ]);
            }
        }
        if let Some(&(merge_offset, switch_offset, default)) = rewrites.wrappers.get(wrapper_index)
        {
            if offset == merge_offset {
                output.extend_from_slice(&[(2 << 16) | BRANCH as u32, default]);
                offset = switch_offset + 3;
                wrapper_index += 1;
                continue;
            }
        }
        let inst = instruction(words, offset)?;
        for (index, &word) in words[offset..offset + inst.count].iter().enumerate() {
            output.push(operands.get(&(offset + index)).copied().unwrap_or(word));
        }
        offset += inst.count;
    }
    *words = output;
    Ok(rewrites.wrappers.len())
}

#[cfg(test)]
mod tests {
    use super::*;

    fn emit(words: &mut Vec<u32>, op: u16, args: &[u32]) {
        words.push((((args.len() + 1) as u32) << 16) | op as u32);
        words.extend_from_slice(args);
    }

    fn label(words: &mut Vec<u32>, id: u32) {
        emit(words, LABEL, &[id]);
    }

    fn branch(words: &mut Vec<u32>, target: u32) {
        emit(words, BRANCH, &[target]);
    }

    fn module() -> Vec<u32> {
        let mut words = vec![MAGIC, 0x0001_0300, 0, 100, 0];
        emit(&mut words, TYPE_INT, &[1, 32, 1]);
        emit(&mut words, 43, &[1, 2, 0]);
        emit(&mut words, FUNCTION, &[3, 4, 0, 5]);
        words
    }

    fn finish(words: &mut Vec<u32>) {
        emit(words, 253, &[]);
        emit(words, FUNCTION_END, &[]);
    }

    fn nested_loops(early_exit: bool) -> Vec<u32> {
        let mut words = module();
        label(&mut words, 10);
        branch(&mut words, 11);
        label(&mut words, 11);
        emit(&mut words, LOOP_MERGE, &[30, 29, 0]);
        branch(&mut words, 12);
        label(&mut words, 12);
        emit(&mut words, SELECTION_MERGE, &[27, 0]);
        emit(&mut words, SWITCH, &[2, 13]);
        label(&mut words, 13);
        emit(&mut words, SELECTION_MERGE, &[14, 0]);
        emit(&mut words, BRANCH_CONDITIONAL, &[6, 14, 30]);
        label(&mut words, 14);
        branch(&mut words, 15);
        label(&mut words, 15);
        emit(&mut words, LOOP_MERGE, &[26, 25, 0]);
        branch(&mut words, 16);
        label(&mut words, 16);
        emit(&mut words, SELECTION_MERGE, &[23, 0]);
        emit(&mut words, SWITCH, &[2, 17]);
        label(&mut words, 17);
        emit(&mut words, SELECTION_MERGE, &[18, 0]);
        emit(&mut words, BRANCH_CONDITIONAL, &[6, 18, 26]);
        label(&mut words, 18);
        if early_exit {
            emit(&mut words, SELECTION_MERGE, &[21, 0]);
            emit(&mut words, BRANCH_CONDITIONAL, &[6, 19, 21]);
            label(&mut words, 19);
            branch(&mut words, 23);
            label(&mut words, 21);
        }
        branch(&mut words, 23);
        label(&mut words, 23);
        branch(&mut words, 25);
        label(&mut words, 25);
        branch(&mut words, 15);
        label(&mut words, 26);
        branch(&mut words, 27);
        label(&mut words, 27);
        branch(&mut words, 29);
        label(&mut words, 29);
        branch(&mut words, 11);
        label(&mut words, 30);
        finish(&mut words);
        words
    }

    #[test]
    fn nested_loop_break_wrappers_are_removed_without_changing_labels() {
        let mut words = nested_loops(false);
        let original = words.clone();
        assert_eq!(normalize(&mut words), Ok(2));
        assert_eq!(words.len(), original.len() - 8);
        let labels = |data: &[u32]| {
            let mut labels = Vec::new();
            let mut offset = 5;
            while offset < data.len() {
                let inst = instruction(data, offset).unwrap();
                if inst.op == LABEL {
                    labels.push(data[offset + 1]);
                }
                offset += inst.count;
            }
            labels
        };
        assert_eq!(labels(&words), labels(&original));
        assert_eq!(normalize(&mut words), Ok(0));
    }

    #[test]
    fn source_continue_expands_selection_tail_with_unique_merge() {
        let mut words = nested_loops(true);
        assert_eq!(normalize(&mut words), Ok(2));
        assert_eq!(words[3], 101);
        assert!(words
            .windows(3)
            .any(|w| w == [(3 << 16) | SELECTION_MERGE as u32, 100, 0]));
        assert!(words
            .windows(4)
            .any(|w| w == [(2 << 16) | LABEL as u32, 100, (2 << 16) | BRANCH as u32, 23]));
        assert_eq!(normalize(&mut words), Ok(0));
    }

    #[test]
    fn phi_bearing_early_exit_merge_keeps_its_switch() {
        let mut words = nested_loops(true);
        let offset = words
            .windows(2)
            .position(|w| w == [(2 << 16) | LABEL as u32, 23])
            .unwrap();
        let phi = [(7 << 16) | 245, 1, 50, 2, 19, 2, 21];
        words.splice(offset + 2..offset + 2, phi);
        let original = words.clone();
        assert!(normalize(&mut words).is_err());
        assert_eq!(words, original);
        assert!(words.windows(phi.len()).any(|w| w == phi));
    }

    #[test]
    fn switch_inside_selection_early_exit_remains_unsupported() {
        let mut words = nested_loops(true);
        let offset = words
            .windows(4)
            .position(|w| w == [(4 << 16) | BRANCH_CONDITIONAL as u32, 6, 19, 21])
            .unwrap();
        words.splice(
            offset..offset + 4,
            [(5 << 16) | SWITCH as u32, 2, 19, 0, 21],
        );
        let original = words.clone();
        assert!(normalize(&mut words).is_err());
        assert_eq!(words, original);
    }

    fn ancestor_tail(with_work: bool) -> Vec<u32> {
        let mut words = module();
        label(&mut words, 10);
        branch(&mut words, 11);
        label(&mut words, 11);
        emit(&mut words, LOOP_MERGE, &[30, 29, 0]);
        branch(&mut words, 12);
        label(&mut words, 12);
        emit(&mut words, SELECTION_MERGE, &[27, 0]);
        emit(&mut words, SWITCH, &[2, 13]);
        label(&mut words, 13);
        emit(&mut words, SELECTION_MERGE, &[14, 0]);
        emit(&mut words, BRANCH_CONDITIONAL, &[6, 14, 30]);
        label(&mut words, 14);
        emit(&mut words, SELECTION_MERGE, &[26, 0]);
        emit(&mut words, BRANCH_CONDITIONAL, &[6, 15, 26]);
        label(&mut words, 15);
        emit(&mut words, SELECTION_MERGE, &[18, 0]);
        emit(&mut words, BRANCH_CONDITIONAL, &[6, 17, 18]);
        label(&mut words, 17);
        branch(&mut words, 27);
        label(&mut words, 18);
        branch(&mut words, 26);
        label(&mut words, 26);
        if with_work {
            emit(&mut words, 128, &[1, 50, 2, 2]);
        }
        branch(&mut words, 27);
        label(&mut words, 27);
        branch(&mut words, 29);
        label(&mut words, 29);
        branch(&mut words, 11);
        label(&mut words, 30);
        finish(&mut words);
        words
    }

    #[test]
    fn silent_ancestor_merge_preserves_bypass_and_has_no_bridge_cycle() {
        let mut words = ancestor_tail(false);
        assert_eq!(normalize(&mut words), Ok(1));
        assert!(words
            .windows(3)
            .any(|w| w == [(3 << 16) | SELECTION_MERGE as u32, 26, 0]));
        assert!(words
            .windows(4)
            .any(|w| w == [(2 << 16) | LABEL as u32, 100, (2 << 16) | BRANCH as u32, 26]));
        assert!(words
            .windows(4)
            .any(|w| w == [(2 << 16) | LABEL as u32, 26, (2 << 16) | BRANCH as u32, 27]));
        assert!(words
            .windows(4)
            .any(|w| w == [(2 << 16) | LABEL as u32, 18, (2 << 16) | BRANCH as u32, 100]));
        assert_eq!(normalize(&mut words), Ok(0));
    }

    #[test]
    fn ancestor_tail_work_and_small_id_bound_fail_atomically() {
        let mut invalid_bound = ancestor_tail(false);
        invalid_bound[3] = 20;
        for mut words in [ancestor_tail(true), invalid_bound] {
            let original = words.clone();
            assert!(normalize(&mut words).is_err());
            assert_eq!(words, original);
        }
    }

    #[test]
    fn genuine_switch_and_non_loop_default_switch_are_unchanged() {
        for cases in [vec![2, 11], vec![2, 11, 1, 12]] {
            let mut words = module();
            label(&mut words, 10);
            emit(&mut words, SELECTION_MERGE, &[13, 0]);
            emit(&mut words, SWITCH, &cases);
            label(&mut words, 11);
            branch(&mut words, 13);
            if cases.len() > 2 {
                label(&mut words, 12);
                branch(&mut words, 13);
            }
            label(&mut words, 13);
            finish(&mut words);
            let original = words.clone();
            assert_eq!(normalize(&mut words), Ok(0));
            assert_eq!(words, original);
        }
    }

    #[test]
    fn phi_instructions_and_predecessor_ids_are_unchanged() {
        let mut words = nested_loops(false);
        let label_offset = words
            .windows(2)
            .position(|w| w == [(2 << 16) | LABEL as u32, 15])
            .unwrap();
        let phi = [(7 << 16) | 245, 1, 50, 2, 14, 51, 25];
        words.splice(label_offset + 2..label_offset + 2, phi);
        assert_eq!(normalize(&mut words), Ok(2));
        assert!(words.windows(phi.len()).any(|w| w == phi));
    }

    #[test]
    fn independent_functions_normalize_separately() {
        let mut words = nested_loops(false);
        let second = nested_loops(false);
        let function_offset = second
            .iter()
            .position(|w| *w == (5 << 16) | FUNCTION as u32)
            .unwrap();
        let mut function = second[function_offset..].to_vec();
        // Remap only function/block IDs; shared type, selector, condition, and
        // literal operands retain their original values.
        let mut offset = 0;
        while offset < function.len() {
            let inst = instruction(&function, offset).unwrap();
            match inst.op {
                FUNCTION => function[offset + 2] = 104,
                LABEL | BRANCH => function[offset + 1] += 100,
                LOOP_MERGE => {
                    function[offset + 1] += 100;
                    function[offset + 2] += 100;
                }
                SELECTION_MERGE => function[offset + 1] += 100,
                SWITCH => function[offset + 2] += 100,
                BRANCH_CONDITIONAL => {
                    function[offset + 2] += 100;
                    function[offset + 3] += 100;
                }
                _ => {}
            }
            offset += inst.count;
        }
        words[3] = 200;
        words.extend(function);
        assert_eq!(normalize(&mut words), Ok(4));
    }

    #[test]
    fn malformed_modules_fail_without_panicking_or_partial_changes() {
        for mut words in [
            vec![],
            vec![MAGIC; 4],
            vec![MAGIC, 0, 0, 0, 0, 0],
            {
                let mut words = nested_loops(false);
                words.pop();
                words
            },
            {
                let mut words = nested_loops(false);
                let offset = words
                    .windows(2)
                    .position(|w| w == [(2 << 16) | BRANCH as u32, 11])
                    .unwrap();
                words[offset + 1] = 999;
                words
            },
        ] {
            let original = words.clone();
            assert!(normalize(&mut words).is_err());
            assert_eq!(words, original);
        }
    }
}
