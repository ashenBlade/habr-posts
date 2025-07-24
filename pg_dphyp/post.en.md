# pg_dphyp: teach PostgreSQL to JOIN tables differently

![Cover](./img/cover.png)

Greetings!

I work in Tantor Labs as a database developer and naturally I am fond of databases. Once during reading the [red book](https://www.redbook.io) I have decided to study planner deeply. Main part of relational database planner is join ordering and I came across DPhyp algorithm that is used in most modern (and not so much) databases. I wonder - is there is anything in PostgreSQL? Surprisingly, nothing. Well, if something does not exist, you need to create it yourself.

This article is not about DPhyp per se, but about what I had to deal with in the process of writing the corresponding extension for PostgreSQL. But first thing first, a little theory.

## Алгоритмы работы планировщиков запросов

The query planner in databases is perhaps the most complex and important component of the system, especially if we are talking about terabytes (especially petabytes) of data. It doesn't matter how fast the hardware is: if the planner made a mistake and started using sequential scan instead of the index scan, that's it, please come back for the result in a week. In this complex component, you can single out the core: JOIN ordering. Choosing the right table JOIN order has the greatest impact on the cost of the entire query. For example, a query like this...

```sql
SELECT *
FROM t1 
JOIN t2 ON t1.x = t2.x
JOIN t3 ON t2.x = t3.x
JOIN t4 ON t3.x = t4.x;
```

...has 14 possible combinations of table JOIN orderings. In general, this is the number of possible representations of a binary tree of `N` nodes, where nodes are tables, [Catalan number](https://en.wikipedia.org/wiki/Catalan_number). We already have 429 representations for 7 tables, and 1430 for 8! Needless to say, from a certain point on, this number becomes so huge that it becomes almost practically impossible to find the optimal plan by simply iterating through all the combinations? The way we are going to search suitable ordering determines architecture of the planner: top-down vs bottom-up.

In the top-down approach (also called goal-oriented) we start from the root and recursively descending down the query tree. The advantage here is that we have the full context in our hands and can use it. The most illustrative example are correlated subqueries: top-down planner can use current context to transform such nested subquery into simple JOIN - this can dramatically improve performance. An example is [the cascades planner](https://www.srdc.com.tr/share/publications/1995/cas.pdf) (roughly speaking, this is the framework), which is used in Microsoft SQL Server: it can easily move the nodes of the query graph under GROUP-BY, which another approach (bottom-up) cannot do without additional help (the architecture assumes a static set of relationships for connection).

Bottom-up is the opposite approach. Here all JOINS are planned first and only then sorting/grouping (+ other operators) operators are added. This approach is used in many databases, including PostgreSQL. Its advantage is scalability, as it allows you to schedule a huge number of JOINS. For example, in the article [Adaptive Optimization of Very Large Join Queries](https://db.in.tum.de/~radke/papers/hugejoins.pdf) presents an approach that allows query planning with several thousand tables by combining different algorithms. The example in the article is SAP, which, due to the constant use of views within other views, can create a query using thousands of regular tables.

There are lots of bottom-up algorithms, so consider the ones that PostgreSQL uses.

### DPsize

During the dawn of RDBMS, no one fully understood how everything should work, and everyone did their best. Then the first dynamic programming algorithm appeared to find the order of connections. Today, everyone (at least in the articles) calls it DPsize.

> Let's remember what an relation is. Relational algebra is built on working with relations. Relation can be understood as a simple data source with its own schema (attributes). The simplest example of a relation is a table, but another important example is a `JOIN`, because in fact it meets the requirements (it gives tuples and have attributes). Next, I will use the term "relation", but where it is important to emphasize the difference, I will say "table".

The idea of DPsize is simple: to create a JOIN of `i` tables, you need to JOIN other relations, which in the sum of the number of tables will give `i`. For example, for `4` we need to connect `1 + 3` or `2 + 2` tables. Actually, this is dynamic programming - the answer of the current step depends on the answer of the previous ones, but the base is a relation of size `1` - ordinary table. The algorithm performs well on an OLTP load when there are few tables, and provides almost optimal query plans, but problems begin when there are too many tables.

As you can see, the time/space complexity of this algorithm is exponential, since at each subsequent step it is required to process even more pairs of relationships. Different databases deal with this in different ways, and PostgreSQL has started using a different algorithm.

### GEQO

GEQO (Genetic Query Optimizer) is a genetic algorithm for finding the optimal query plan. If you try to run a query in PostgreSQL, first for 12 tables, and then for 13, you will be surprised, because the time spent has decreased from a few seconds to almost ten *milliseconds*. Why? Because GEQO has entered the game (`geqo_threshold` setting is `12` by default). The fact is that this is a randomized algorithm, its core idea can be described as follows: first, we build *at least some* query plan, and then we carry out several iterations (this is determined by the configuration), in each of which we randomly change some nodes, and in the next iteration there is a plan with the best cost. Hence the name "genetic" due to the fact that the strongest (in our case, the cheapest) pass into the next generation (iteration).

## DPhyp

Let's move on to the main topic — the DPhyp algorithm. DPhyp is a dynamic programming algorithm for JOIN ordering. Its main idea is that the query itself contains guidance on how tables should be joined. So why not use it? The main problem is in the query representation itself. I mentioned earlier that a query can be represented as a graph, but is it possible to do this for the purposes of iterating over JOINS? To understand the difficulty, let's look at an example from the paper:

```sql
SELECT *
FROM R1, R2, R3, R4, R5, R6
WHERE R1.x = R2.x AND R2.x = R3.x AND R4.x = R5.x AND R5.x = R6.x AND
      R1.x + R2.x + R3.x = R4.x + R5.x + R6.x
```

Yes, we see that there are several *explicitly* connected tables — for them we can create edges in our graph (for example, `R1 - R2`), but what about the last predicate, which actually connects multiple tables? This problem is solved by DPhyp, a dynamic programming algorithm (DP) based on *hypergraphs* (hyp — hypergraph). You should not be afraid, I assume that you are familiar with an ordinary graph — a set of nodes connected by edges, but a hypergraph is a set of hypernodes connected by hyperedges:

- hypernode — is a *set* of regular *nodes*;
- hyperedge — is an edge, connecting 2 *hypernodes*.

In example query we have next hyperedges:

1. `{R1} - {R2}`
2. `{R2} - {R3}`
3. `{R4} - {R5}`
4. `{R5} - {R6}`
5. `{R1, R2, R3} - {R4, R5, R6}`

If there is only one relation in a hypernode, then it is called a *simple hypernode*. Similarly, if a hyperedge connects two simple hypernodes, then it is a *simple hyperedge*. Thus, the first four of the list above are simple hyperedges.

To create a plan for a set of `i` nodes, we need to handle *not all possible* pairs that give `i` in total, but all pairs of hypernodes that give the same set. This is a pair of two disjoint sets: a `connected subgraph` (csg, subgraph) and a `connected complement` (cmp, complement). These abbreviations will often appear in the post.

To make everything work optimally, we need to introduce a small restriction — the order of the nodes. There should be an order above the nodes (i.e. tables), roughly speaking, they should be numbered (numbering is used most often) so that we can compare and sort them later. To understand why, let's look at the core of the algorithm — neighborhood.

During the operation of the algorithm, we use neighbors to move from one hypernode to another. The article provides a mathematical definition, but the easiest way is to say that the neighbors of a hypernode are other reachable nodes. It is also required that this set be minimal, otherwise we will process the same hypernodes several times. This is where the order is needed — when we bypass the edges to find neighbors, we add only the *representative* of the hypernode, its *minimal element*, to the set. Next, you just need to check that the other edges do not contain nodes that have already been added.

The last important detail of the algorithm is the *excluded set*. In DPsize, as an optimization, we start iterating over complementary pairs not from 0, but from the next index, or otherwise we will try to connect ourselves to ourselves, and process some pairs twice. The idea is about the same here, we keep track of nodes that are not worth considering (excluded) and check this almost everywhere, even when finding neighbors. Due to this, we can avoid considering the same set twice.

The main logic of the algorithm in the article is represented by five functions, and the core of the algorithm can be briefly described as follows: we need to find a csg-cmp pair that combines the entire query, so using neighbor search, csg and cmp will increase alternately/recursively. The only question is where to start. The answer here is this — we start iterating over all nodes starting *from the end*, and add to *excluded* set all nodes that are smaller than the current one. As a result, we start with a simple hypernode that no one has considered, and then recursively expand it and find the cmp for it using neighbors.

Actually, it's not difficult to understand the functions now:

- `Solve` — algorithm's entry point. Iterate over all simple hypernodes from end and invoke `EmitCsg` and `EnumerateCsgRecursive`;
- `EmitCsg` — accepts *fixed* csg, for which we find corresponding cmp, and then invoke `EmitCsgCmp` and/or `EnumerateCmpRecursive`;
- `EmitCsgRecursive` — accepts csg, which is expanded using it's neighborhood, then invoke `EmitCsg` and/or `EnumerateCsgRecursive`;
- `EnumerateCmpRecursive` — accepts *fixed* csg with cmp, which is expanded using cmp's neighborhood, then invoke `EmitCsgCmp` and/or `EnumerateCmpRecursive`;
- `EmitCsgCmp` — creates query plan for given *fixed* csg/cmp pair.

The main idea is clear. And as an example, the article also has a step-by-step illustration of how the algorithm works for the query from the example:

![Algorithm tracing](https://raw.githubusercontent.com/ashenBlade/habr-posts/master/pg_dphyp/img/paper-trace.png)

1. Iteration performed backwards, so start from `R6` (the highest index), but it does not have any neighbors, because all other nodes are in excluded set.
2. Move on to `R5`.
3. Find neighbor `R6` and create cmp `{R6}`.
4. Expand csg itself to `{R5, R6}`.
5. Move on to `R4`.
6. Find neighbor `R5` and create cmp `{R5}`.
7. Expand cmp to `{R5, R6}` (`R6` is a neighbor of `R5`).
8. Move back to 6 step and expand csg to `{R4, R5}` (earlier cmp is expanded).
9. Find neighbor `R6` for csg `{R4, R5}` and create corresponding cmp `{R6}`.
10. Expand csg `{R4, R5}` to `{R4, R5, R6}`.
11. Move on to `R3` (`{R1, R2}` are excluded), but it does not have any neighbors:
    - All hypernodes, we have direct edge with (`R1` and `R2`), are excluded (have lower index);
    - Representative of left hyperedge of complex hyperedge (`min({R1, R2, R3}) = R1`) also in excluded set, so this hyperedge is not considered.
12. Move on to `R2`.
13. Find neighbor `R3` and create cmp `{R3}`.
14. Expand csg to `{R2, R3}`.
15. Move on to `R1`.
16. Find neighbor `R2` and create cmp `{R2}`.
17. Expand cmp to `{R2, R3}` (`R3` is a neighbor of `R2`).
18. Expand csg to `{R1, R2}` (`R2` is a neighbor of `R1`).
19. Find neighbor `R3` and create cmp `{R3}`.
20. Expand csg to`{R1, R2, R3}` (`R3` is a neighbor of `R2`).
21. Find neighbor `R4` and create cmp `{R4}` (use representative `R4` of complex hyperedge `{R1, R2, R3} - {R4, R5, R6}`).
22. Expand cmp to `{R4, R5}` (`R5` is a neighbor of `R4`).
23. Expadn cmp to `{R4, R5, R6}` once again (`R6` is a neighbor of `R5`).
24. Move back to step 20 and expand csg using `{R4}`.
25. Then add `{R5}`.
26. Finally add `{R6}`.

The algorithm is quite good and intuitive. Can I add it to PostgreSQL? Yes, and it has always been possible! For such case, there is a `join_search_hook` — a hook that allows you to replace vanilla JOIN search algorithm.

## pg_dphyp extension

The idea to create this extension came to me by chance: I was studying different JOIN algorithms and came across it. An Internet search didn't turn up anything like this for PostgreSQL, and I realized that I had to do it myself. Who immediately wants to see what happened, [here is the link to the repository](https://github.com/TantorLabs/pg_dphyp). In the context of the core of the algorithm, I did not bring anything new, on the contrary, I copied more from others. Before starting to implement my own, I looked at the implementations in several databases, in particular, YDB, MySQL and DuckDB. If someone wants to learn DPhyp by code, I recommend looking at [code YDB](https://github.com/ydb-platform/ydb/blob/c23202bc294cf703741f1ea6ac30786578a58920/ydb/library/yql/dq/opt/dq_opt_dphyp_solver.h) — Clean and clear, very easy to read. But I didn't start with YDB myself, but with MySQL, and now its implementation has been significantly changed and optimized, it won't be possible to figure it out right away, only based on the comments and with the initial understanding of DPhyp itself.

In the implementation, I tried to be closer to the paper and make the minimum number of changes. For example, the names of functions and some variables are the same as in the paper. But although there are practically no changes in the core of the algorithm, they exist at the operational decision-making level, and the first of them concerns the representation of sets.

### Представление множеств

Sets are the workhorse of the algorithm, underlying all of its effectiveness. Different DBMS do this in different ways, for example:

- DuckDB uses [numbers directly and store them in an array](https://github.com/duckdb/duckdb/blob/73f85abbbdd38555ef7afa08090dfb4b10120df8/src/include/duckdb/optimizer/join_order/join_relation.hpp#L24);
- YDB - [`std::bitset<>` from C++ standard library](https://github.com/ydb-platform/ydb/blob/c23202bc294cf703741f1ea6ac30786578a58920/ydb/library/yql/dq/opt/dq_opt_join_cost_based.cpp#L341);
- MySQL - [8-byte number as bitset](https://github.com/mysql/mysql-server/blob/ff05628a530696bc6851ba6540ac250c7a059aa7/sql/join_optimizer/node_map.h#L40).

Anyone who works with PostgreSQL knows that there is [its own implementation of the set — `Bitmapset`](https://github.com/postgres/postgres/blob/62a47aea1d8d8ea36e63fe6dd3d9891452a3f968/src/include/nodes/bitmapset.h#L49). It is used everywhere and most common use case is to store relation IDs. It would seem that you should just take it and use, but the problem is that there are a lot of operations on sets, and `Bitmapset` creates a new copy every time it changes, that is, these are unnecessary memory allocations. In PostgreSQL, this problem often does not occur, because after creating the `Bitmapset` it rarely changes, but not in my case, and this is critical.

In first implementation, I have solved the problem by implementing two approaches at once — I have created two files, where in one I used `bitmapword` (an 8-byte number/bitset, as in MySQL), and in the other I have used `Bitmapset` for complex queries (with more than 64 tables). But this happened at the very beginning of development, when I still did not really understand the subtleties of the algorithm. So after a while I dropped updating the file with the "Bitmapset" (I decided to add changes later), and finally completely deleted it.

Now I use the representation of a set using a number, and this does not cause many problems. The basic operations with a set are performed with simple bitwise operations: `|`, `&` and `~`. But there are a couple more operations that are important for the algorithm itself, for example, iterating over the elements of the set in the process of calculating neighbors. There are many such operations, so I put them in a separate [header file](https://github.com/TantorLabs/pg_dphyp/blob/b5406651b8f95743042be847b38c06b75bd23670/simplebms.h).

Another interesting operation is iteration over all subsets, this is necessary to expand csg/cmp. Since a set is a number, the operation is performed with a number. In MySQL and YDB, this was solved using the bit trick `(init - state) & state`, which, when applied continuously, behaves like an increment, but only the bits of the set change. This implementation is currently [in use](https://github.com/TantorLabs/pg_dphyp/blob/b5406651b8f95743042be847b38c06b75bd23670/pg_dphyp.c#L541). For example, for the set `01010010` we get the following subsets:

```text
00000010    001
00010000    010
00010010    011
01000000    100
01000010    101
01010000    110
01010010    111
```

On the left side - number as set in binary representation, and on the right side - it's bitmask. As we iterate by incrementing, so we can say, that bitmask in number representation is the same as iteration number. Next, we will use such property often.

### DP-table

DPhyp is a dynamic programming algorithm, and it has its own table for tracking execution status. If you look at the algorithm from the paper, you can see that this table is used only for storing ready-made query plans. In PostgreSQL, the `RelOptInfo` structure is used to store list of query plans (`Path` structures) for fixed set of relations, and *it is already stored in the hash table*. It seems that you don't have to think about creating your own hash-table, but no. The problem lies in PostgreSQL itself, or rather, its `FULL JOIN` processing.

For this type of JOIN, only the equality predicate is currently supported, and in the code, when such a predicate occurs, all relations in the left and right parts fall into separate lists that are planned *independently*. For example, for query `SELECT * FROM t1 FULL JOIN (SELECT t2.x x FROM t2 JOIN t3 ON t2.x = t3.x) s ON t1.x = s.x` we have to run 2 JOIN algorithms: `{t2, t3}` and `{t1, {t2, t3}}`.

This is the reason why internal tables use their own indexes (that is, DPhyp node indexes do not necessarily correspond to relationship indexes). Therefore, even if we temporarily turn `bitmapword` into `Bitmapset`, which will require additional memory allocation, I will not be able to do this if the relation indexes exceed 64 (the maximum value for an eight-byte number).

Hash table in PostgreSQL (`HTAB` structure) has one specificity - it store element and it's key in same structure (key must be first member of element's structure). But builtin hash table for storing `RelOptInfo`s hash `Bitmapset` key and pg_dphyp uses `bitmapword` (the reason discussed above). So extension creates and maintains it's own hash table.

### Hypergraph creation

Another important task is the building of the hypergraph itself. The problem is that unlike YDB or MySQL, we don't run the show and obey the rules of the database.

There doesn't seem to be a problem, I can go through all the predicates used in the query and create edges from them. Actually, this is how it is implemented now. But the devil is in the details.

Such hyperedge information stored in 3 separate places:

1. `PlannerInfo->join_info_list` — list of non-INNER non-INNER JOIN predicates.
2. `RelOptInfo->joinclauses` — list of JOIN predicates (require more than 1 relation).
3. `PlannerInfo->eq_classes` — list of equivalence classes.

The first is the simplest. This list contains restrictions that are imposed by various non-INNER (i.e. LEFT/RIGHT/FULL, etc.) JOINS. It has 2 pairs of parts: syntactic constraints and the minimum ones (the only needed for the calculation). Hyperedges are created a hyper-edge for both pairs just in case.

The second one has difficulties in terms of the expression itself — expression may not be binary, or it may be, but the same relation can be mentioned on both sides. For such moments, I added my own concept — cross join set (cjs). It's just a set of relations that need to connect with each other (clique). For each relation in CJS, I create simple edge (each with each one). This solves the problem that some predicates may be missing. In example `WHERE sample_func(t1.x, t2.x t3.x)` have CJS of `{t1, t2, t3}` - this is not binary predicate and we create `{t1} - {t2}`, `{t2} - {t3}` and `{t1} - {t3}` hyperedges.

Lastly, the third - equivalence classes. The equivalence class is a PostgreSQL mechanism by which it determines that some expressions are equal to each other. This is not only used in predicates (to deduce equivalences). For example, expressions in `ORDER BY` or `GROUP BY` are represented by an equivalence class (possibly degenerate, with a single element). But now it's not about that, but about how it shoots. Such equivalence classes appear when there are equality expressions in the query, and even one is enough to create an equivalence class. As you might guess, they are also used for JOINS. When I encounter an equivalence class with multiple relations, I have to create hyper edges for each pair. Therefore, such queries with only equality under the hood turn into a clique (as you can mention - logic is same as for CJS).

For example, consider this query:

```sql
SELECT * FROM t1 
         JOIN t2 ON t1.x = t2.x AND t1.y > t2.y
         JOIN t3 ON t1.x + t2.x = t3.x
    LEFT JOIN t4 ON t4.x = t3.x;
```

It contains:

- equivalence classes: `{t1.x, t2.x}` and `{t1.x + t2.x, t3.x}`;
- JOIN predicate: `t1.y > t2.y`;
- special (LEFT) JOIN predicate: `t4.x = t3.x`.

We will create the following hyperedges:

- `{t1} - {t2}`
- `{t3} - {t1, t2}`
- `{t3} - {t4}`

### Disconnected subgraphs

Another problem arises from the above. What happens if we get a disconnected graph (a forest)? In this case, the algorithm will not be able to create result plan. More precisely, it will create a plan for each connected component in the forest, but it will no longer be able to do so for the *entire* query. Moreover, this problem may appear not only because of the `CROSS JOIN` or `,`, but also because of the external parameters of the subqueries. I'll give you an example:

```sql
SELECT * FROM t1
WHERE t1.x IN 
    (SELECT t2.x FROM t2, t3 WHERE t2.x = t1.x AND t3.x = t1.x);
```

> I discovered this when I ran `\d` (to display all tables) in `psql`, and it turned out that there was an example query.

In the subquery, `t1.x` is a parameter, but we have a forest in the output graph, because there is no relation ID for `t1` in the subquery. On one hand, in practice disconnected graphs are quite rare to waste resources to detect such a thing, moreover, we can waste extra time (just double the planning time without any payload). Therefore, I left it up to the users to decide what to do.

There is setting `pg_dphyp.cj_strategy`, which accepts 3 values:

- `no` – if we failed to build result plan, then invoke DPsize/GEQO with initial values;
- `pass` – if we failed to build result plan, then collect plans for all connected components and pass them to DPsize/GEQO;
- `detect` – find all connected components during initialization and create dummy hyperedges for them.

You can think that `no` is superfluous, but it is not because the plans created for these subgraphs may not be optimal due to the fact that there may be implicit connections between disconnected subgraphs that could help create an optimal plan. Therefore, the final plan may also be suboptimal.

The question remains: how to find disconnected graphs? The answer is simple - [Union-Set algorithm](https://github.com/TantorLabs/pg_dphyp/blob/b5406651b8f95743042be847b38c06b75bd23670/pg_dphyp.c#L953). For performance, an optimized version is used: ranking and leader updating.

### Hypergraph representation

One more task is storing the hypergraph. The graph can be stored as a list of edges, or it can be an adjacency table. But for a hypergraph, only the first option remains, since it is practically impossible to build an adjacency table for hypergraph (hyperedge connects hypernodes which can have more than 1 node on both sides).

Well, we've decided to use list of hyperedges, but is it possible to apply any optimizations? Yes, we can. For almost all implementations (for example, [YDB](https://github.com/ydb-platform/ydb/blob/c23202bc294cf703741f1ea6ac30786578a58920/ydb/library/yql/dq/opt/dq_opt_join_hypergraph.h#L84) and [MySQL](https://github.com/mysql/mysql-server/blob/ff05628a530696bc6851ba6540ac250c7a059aa7/sql/join_optimizer/hypergraph.h#L69)) there is an optimization for simple hyperedges. Let me remind you once again that a simple hyperedge has set of a single element on both sides. This optimization uses this fact to store all nodes for which we have simple hyperedge in single set. Next, during work we need to do a single operation (i.e. subset or overlaps) to check multiple edges at once. Such a set is often called a "simple neighborhood", I think it's clear why. This optimization is also used in extension, but I went a little further, and I store this simple neighborhood for each hypernode (in the `HyperNode` structure). This simplifies the work a bit, since you don't need to spend time on additional iteration across nodes, and it takes up only 8 bytes of space.

But this is still not the end. Earlier, I said that for the same expression in the query text, multiple instances of `RestrictInfo` (representation of the expression in the source code) can be created, but with a different set of indexes of the required relations. At the same time, I said that I couldn't process these additional relation IDs, so I could end up with a lot of duplicates of expressions. This can lead to lots of wasted work. I solved this using [sorting](https://github.com/ashenBlade/pg_dphyp/blob/0cdc5b410d3bce41398a6646c576cca77994b6e3/pg_dphyp.c#L1096): all hyperedges stored in sorted array. So adding new hyperedge is adding new element to sorted array - binary search can quickly find duplicates and prevent such bloating.

### Создание плана

If you have read original paper then you known that DPhyp is not only about JOIN ordering, but it also gives some tips for effective query plan creation. This is an important point, since some JOIN operators are not commutative (for example, `LEFT JOIN`), and such JOINs impose restrictions. But that's not all. Let me remind you that DPsize (builtin planner) has high cohesion with the code base — so much so that it is impossible to create a plan for the `i` tables without finding the optimal plan for the underlying ones.

According to DPhyp query plan built in `EmitCsgCmp`. Initially, that's what I did:

1. Take 2 `RelOptInfo` (csg/cmp, left/right) and invoke `make_join_rel` with them.
2. Call `generate_useful_gather_paths` to create gather paths (parallel).
3. Call `set_cheapest` to find the best plan.

But if you look inside, you will see that the `generate_useful_gather_paths` and `set_cheapest` functions go through all the paths created so far, that is, as the program runs, the time spent on its execution will only increase. Moreover, `set_cheapest` function will update cheapest paths found, so these paths can be used to create new `RelOptInfo`s - that is the problem, because in such approach we can use not cheapest plan, but first created.

Due to these facts, I switched to a DPsize-like approach. Keep a list of candidate hypernode pairs that can create target for each hypernode. At the end recursively build target `RelOptInfo`s using this list and finally invoke `generate_useful_gather_paths`/`set_cheapest`.

Yes, there is a chance that there will be a pair inside that will not be able to create a query plan, and I will waste time, but the overhead of the initial approach (with a constant call to `set_cheapest`) is still greater, and the probability that some pair of hyper nodes will not create a useful JOIN is extremely low due tofor the very idea of DPhyp.

## Optimal neighborhood calculation

Finally, we've come to the most interesting part - how we're going to traverse the neighbors. We can highlight three functions in the algorithm that are engaged in creation of csg/cmp pairs, and in them we need to calculate neighbors as many as four times: `EmitCsg`, `EnumerateCsgRec` and `EnumerateCmpRec`. Everything starts to look more intimidating when you realize that the algorithm involves iterating over all possible subsets of neighbors ([power set](https://en.wikipedia.org/wiki/Power_set ) without the empty set) is $2^i - 1$ combinations.

Yes, we have added simple neighborhood optimization, but still we need to process all the nodes in the subsets. Totally, we get $\sum_{k = 1}^{n}kC_{n}^{k}$ nodes that need to be processed for all subsets.

> The number of different combinations of subsets of $k$ elements is $C_{n}^{k}$, but for each subset of size $k$, each of its elements must be processed, and there are $k$ of them. In other words, the number of elements in all subsets of size $k$ is $kC_{n}^{k}$.

First, let's look at the definition of neighborhood:

$$
E\darr'(S, X) = \{v|(u, v) \in E, u \subseteq S, v \cap S = \emptyset, v \cap X = \emptyset \} \\
E\darr(S, X) = minimal\ set: \forall_{v\in E\darr'(S,X)} \exists v' \in E\darr(S,X): v' \subseteq v \\
\mathcal{N}(X, X) = \cup_{v \in E\darr(S, X)} min(v)
$$

1. The set $E\darr'$ is created — (interesting) hyperedges belonging to the current hypernode and pointing beyond it.
2. This set is minimized to eliminate subsumed hypernodes (so there are no overlaps and potential duplicates).
3. Only representatives (the smallest elements) are selected from remaining hypernodes.

The second step (finding the *minimum* set) is a complex computational task, so many databases use optimization: as nodes are traversed representatives added into the neighbors *only* if this hypernode does not intersect with already computed neighborhood. This approach is used by [MySQL](https://github.com/mysql/mysql-server/blob/ff05628a530696bc6851ba6540ac250c7a059aa7/sql/join_optimizer/subgraph_enumeration.h#L314) and [YDB](https://github.com/ydb-platform/ydb/blob/c23202bc294cf703741f1ea6ac30786578a58920/ydb/library/yql/dq/opt/dq_opt_dphyp_solver.h#L438).

Does it make sense to optimize this fragment? There are quite a lot of comments in the MySQL code that describe certain decisions made (most often based on micro benchmarks). One of the [such comments](https://github.com/mysql/mysql-server/blob/ff05628a530696bc6851ba6540ac250c7a059aa7/sql/join_optimizer/subgraph_enumeration.h#L280) is written for the neighborhood computation function `FindNeighborhood`:

> ...
> This function accounts for roughly 20–70% of the total DPhyp running time, depending on the shape of the graph (~40% average across the microbenchmarks)
> ...

That is, almost all the time the algorithm is running is occupied by the logic of calculating neighbors, and therefore, yes, there is a point in optimizing it.

### MySQL approach

The idea implemented in MySQL is based on the property of subsets that we get when iterating incrementally. You can notice that in the next step we often get a *superset of the previous step*, and very often (in most cases - every next step) the first bit is constantly switched from `0` to `1`. And if we switch it from `0` to `1`, then we get a subset! For example, `0011101` is subset of `0011100`.

There is an immediate desire to simply take the previous neighbors and add this first element, but the question arises — is this correct? Yes, that's correct. If we look again at the definition of neighbors from the article, we will not see any restrictions on the order of node traversal, which means that we can take one set and enlarge it with some new element.

To make the idea clearer, let's look at an example of iterating over a subset of 4 nodes. Here is an example from a comment:

```text
0001
0010 *
0011
0100 *
0101
0110 *
0111
1000 *
1001
1010 *
1011
1100 *
1101
1110 *
1111
```

With an asterisk, I indicated the places where we will have to fully calculate the neighbors, that is, you must clear the cache, but after that — on the next subset — it will be enough for us to process only one node (the first one). If everything is done honestly (without optimizations), then for an initial set of size 4, - 32 nodes will have to be processed, and using this heuristic, only 20.

It was also pointed out in the comment that there are optimal subset traversal orders that will give even greater gains. For a set of four elements, it will be like this (in the comment in the code, probably a little bit — after 0111 comes 1110, which does not allow optimal calculation of neighbors, so here is my corrected version).

```text
0001 
0010 *
0011 
0100 *
0101
0110 *
0111
1000 *
1010
1100
1001 *
1011
1101
1110 *
1111
```

The idea is simple: we iteratively shift the rightmost (first) bit to the left until it hits the left side of sequence of ones, and when this left side is over (all 1), we start a new digit — we update the cache at the beginning of a new digit or when this bit hits a sequence of ones.

Here we only need 15 iterations (nodes to process). In practice, even such a heuristic with tracking the last bit gives a significant increase, requiring almost 2 times fewer iterations.

Is the optimal order known for a larger set? Yes, but only for five elements (I won't give example of it here, you can see it in the same comment).

If someone thought, "just take a shortcut and that's it," then alas. Do not forget that right now you are just looking at a *bitmask* of included/excluded elements from the set. In reality, the elements in the bitmask are sparse, for example, iterating over the set of three elements `00101001` will look like this (on the left is a subset, on the right is this mask/iteration number):

```text
00000001   001
00001000   010
00001001   011
00100000   100
00100001   101
00101000   110
00101001   111
```

<spoiler title="Limitations of caching scheme">

The neighbor caching scheme only works when the set of excluded nodes *is fixed*. This requirement is satisfied by the last cycles in the `Enumerate*` functions, when the excluded set is fixed during all iterations (in the definition from the article, these functions recursively call themselves, but with a fixed set of excluded nodes $X\cup\mathcal{N}(S,X)$).

But even this is enough to increase performance, since the other two places of traversing subsets are:

- `EnumerateCmpRecursive`, but it only invokes `EmitCsgCmp` which does not require to compute neighborhood;
- `EnumerateCsgRecursive`, but it invokes `EmitCsg`, which require new excluded set (it is not fixed, depends on iteration).

To understand why neighbors cannot be cached if the set of excluded nodes is changing, let's look at a specific example. Call `EnumerateCsgRecursive` with a hypernode $S = 000110$ and excluded set $X = 000111$. It has many neighbors, whose subsets we will iterate over, $\mathcal{N} = 1110000$ (that is, note that $0001000$ is free for now and does not belong to anyone):

We want to efficiently compute neighbors, and for this purpose we cache them. The question is: what should I cache? The immutable part is $S$, to which a subset of neighbors is added. We decide to cache it, but then this situation arises: for some node there is a hyperedge, the right side of which is $1001000$ (remember that previous free $0001000$ is used).

```text
S =  000110
X =  000111
N = 1110000  <-- current neighborhood
R = 1001000  <-- neighbor candidate
```

How can this hyperedge be processed correctly? We have two possible outcomes (depending on whether we have added this right part to excluded set or not):

- all neighbors *were not added* to the set of excluded nodes. Then the node $0001000$ is added to the neighbors for `EmitCsg`, which will be passed everywhere. But this means that for iterations in which $1000000$ is involved, the logic of computing neighbors in `EmitCsg` will be violated, since this element will be in the excluded set, and accordingly *the edge should not be taken into account*;
- all neighbors *were added* to the set of excluded nodes. Then we get the other side — in iterations where the $1000000$ element is not involved, *some node will be missing*.

If you look at the MySQL code, you can see that they still use caching, but why? The fact is that they compute the neighbors for a subset, and then *add the excluded nodes and the original neighbors to the resulting neighbors*. Excluded ones are added according to the logic of `Enumerate*` functions, since excluded sets are passed to them as input, which contain all previously found sets, although only a small subset could be passed by a recursive call. For example, during operation, `EnumerateCsgRecursive` recursively called itself several times, and each time the set of neighbors consisted of two elements, but recursive calls passed only one, although the accumulated excluded nodes are the union of all found neighbors. That is, the set of excluded nodes that have been passed to us can contain all the nodes that will be valid neighbors in `EmitCsg` (since it resets the current excluded ones and starts using its own).

Example: after several recursive calls, `EnumerateCsgRecursive` has $S = 1010001011$, $X = 1111111111$, but when invoking `EmitCsg`, the excluded set will be reset to $X = 0000000011$ (for example, because the smallest node is 2). Then adding $X\\S = 0101110100$ will simply mean that, just in case, we want to process those nodes that were neighbors of previous calls, but did not get into the current CSG set.

The original neighbors are added according to the same logic, but in order not to violate the correctness of the algorithm: all nodes that are in the resulting subgraph are removed from the resulting set. The developers initially know that the neighbors obtained in this way may not contain all the neighbors, so they generally add everything that may be true, even if this leads to excessive calculations. Here is a part of this code with comments describing the reasons in more detail (`lowest_node_idx` is the index of the node with which the current iteration in `solve` is running):

```cpp
// EnumerateComplementsTo() resets the forbidden set, since nodes that
// were forbidden under this subgraph may very well be part of the
// complement. However, this also means that the neighborhood we just
// computed may be incomplete; it just looks at recently-added nodes,
// but there are older nodes that may have neighbors that we added to
// the forbidden set (X) instead of the subgraph itself (S). However,
// this is also the only time we add to the forbidden set, so we know
// exactly which nodes they are! Thus, simply add our forbidden set
// to the neighborhood for purposes of computing the complement.
//
// This behavior is tested in the SmallStar unit test.
new_neighborhood |= forbidden & ~TablesBetween(0, lowest_node_idx);

// This node's neighborhood is also part of the new neighborhood
// it's just not added to the forbidden set yet, so we missed it in
// the previous calculation).
new_neighborhood |= neighborhood;
```

</spoiler>

To implement their singleton cache, they use the [class `NeighborhoodCache`](https://github.com/mysql/mysql-server/blob/ff05628a530696bc6851ba6540ac250c7a059aa7/sql/join_optimizer/subgraph_enumeration.h#L163) and they transitively pass it almost everywhere. Its logic is simple: before starting the neighbor search, we find the delta of the sets, but in fact we check that the last bit is set, and at the end we save the calculated neighbors — but only if the first bit (`taboo bit`) is not set - just because this will not contribute to any further neighborhood sets anymore (this is last bit can be set).

As soon as I understood the idea, I rewrote the code to myself almost word for word, only rename `taboo` to `forbidden`. The code lived in this form for quite a one week, but then I realized — GPLv2! The MySQL code is distributed under the GPLv2 license, and considering that I rewrote almost everything word for word (at that time, probably not even fully understanding the idea itself), I violated this license: the extension uses MIT - they are incompatible! Then I faced the question — should I throw out this good optimization and make the code slow, or leave it, but change the license to GPLv2? As a result, I chose the first one, and this was the beginning of an exciting several-week thinking on how to optimize this combinatorics.

### Suffix cache

My task is to optimize the algorithm, but in such a way that it is not a MySQL tracing paper. The idea itself is clear: why to compute the entire set if you can simply extend it with this delta. We somehow need to find either a template or something that will help us detect such a subset.

But what if you look at it from the other side? Literally from the other side. Indeed, our subset iteration method has a good property: the upper part (MSB) changes much less frequently than the lower part (LSB). So why don't we use this property? We will just cache something that rarely changes!

But what is "rarely changing" in essence? In the first idea, I took the closing ones for this constant — the sequence of the last (leftmost) ones (with a fixed digit) in the number can only increase during increment, that is, it is enough only to count the neighbors for this sequence, and then add the changing one. We can separate the base (immutable) part with a simple bitmask, and we know its size (calculated).

> If we recall the optimal iteration sequence proposed by MySQL, then we can see that this strategy is ideally suited for such a sequence.

How do we track the change in this base part? It's easy, because this is binary arithmetic: we just keep track of how many iterations until the next new `1`, and then divide by `2` and wait calculated number of iterations. When a new digit begins, we simply reset the counter and repeat. The algorithm is as follows:

1. Initialization:
   1. `nb_cache = 0` — cached neighborhood.
   2. `nb_cache_subset = 0` — bitmask of cached neighborhood above.
   3. `next_update = 0` — number of iterations until next cache update.
   4. `prev_update = 0` —  saved `next_update` value.
2. For each iteration (starting from 1):
   1. If `next_update == 0` (new digit added to base part):
      1. Calculate neighborhood using the first element (add to cached neighborhood).
      2. `next_update = prev_update / 2` — next new 1 will be right after half the number of previous iteration number (binary arithmetic).
      3. `prev_update = next_update` — reset value.
      4. `nb_cache = *currently computed neighborhood*`.
      5. `nb_cache_subset = *current subset*`.
   2. If there is only single element in subset (of iteration number is a power of 2):
      1. Calculate neighborhood using the only element.
      2. `next_update = 2^*digit number - 2*` — number of iterations before new digit is added to base part.
      3. `prev_update = next_update`.
      4. `nb_cache = *currently computed neighborhood*`.
      5. `nb_cache_subset = *current subset*`.
   3. Otherwise:
      1. Remove `nb_cache_subset` from current subset (so find that delta).
      2. Calculate current neighborhood as `nb_cache` + neighborhood of delta.

### 1-layered cache

Is this a good idea? Yes, but only a starting point for further reasoning, but in practice, this caching scheme will give an increase only at the end, when almost everything is filled with ones (since I have not found a way to iterate over optimal subsets, it means iterating incrementally).

Let's go back to the beginning. We discussed that rarely changing parts need to be cached. For this part, we took the sequence of closing ones. It rarely changes, but note that this part is variable, that is, you need to track its size. Now we will *make base part fixed*, that is, we will *cache a certain suffix* of the set.

Which will give a greater increase — caching of closing ones or MSB suffix? Let's count on the same set of 4 elements. For the caching scheme of the leading `1` we have the following computation scheme (the second column shows the number of processed nodes, and an asterisk marks the cache update locations):

```text
0001  1
0010  1  *
0011  2
0100  1  *
0101  2
0110  2  *
0111  1
1000  1  *
1001  1
1010  1
1011  2
1100  2  *
1101  1
1110  3  *
1111  1 
```

In total, 22 elements need to be processed — two more than the taboo-bit scheme.

With the MSB suffix caching scheme, you first need to answer the question — what is its size? If we take a little, we will perform computations of variable part a lot, on the contrary, if we take too much, we will calculate this MSB often. Basically, you can calculate everything, because it's the same binary math. But for clarity, let's take the MSB equal to 2:

```text
0001  1
0010  1
0011  2
0100  1  *
0101  1
0110  1
0111  2
1000  1  *
1001  1
1010  1
1011  2
1100  2  *
1101  1
1110  1
1111  2
```

There are already 20 elements here — the same number as in the MySQL approach, but less than the closing ones approach.

> Here, every time I updated the cache - computed the neighbors completely. You might have noticed that when calculating the cached part for the neighbors, we could use the same optimization with the last bit — add this last element to the cached neighbors. This optimization will allow us to achieve only 16 iterations, which is much more efficient.
> Back then I missed this moment, but it was for the best — you will soon find out why.

The scheme with caching of the fixed part of the MSB proved to be better, and besides, it is configurable (the parameter is the size of the cached part). As a result, we choose a caching scheme with MSB suffix. Next, I will call this immutable part (which we use as the starting value for calculating neighbors) - base. The algorithm for working with it is quite simple: every $2^{len(64 - MSB)}$ iterations we save this basic part to the cache, and then just use it as a starting point when calculating neighbors.

### 2-layered cache

Wait, we're working with sets, and any set can be created from the previous one by adding single element! For example, the set `011010` can be constructed from any `001010`, `010010` or `011000`. But it also means that you can create the current set by simply adding the outermost element from the beginning. The same initial example with the taboo bit is a special case when we added the first element.

Let's draw a graph of 4 element set and see how we can create the current set from the previous one:

```text
0000 <+  <+  <+
   ^  |   |   |
0001  |   |   |
      |   |   |
0010 -+   |   |
   ^      |   |
0011      |   |
          |   |
0100 <+  -+   |
   ^  |       |
0101  |       |
      |       |
0110 -+       |
   ^          |
0111          |
              |
1000 <+  <+  -+
   ^  |   |
1001  |   |
      |   |
1010 -+   |
   ^      |
1011      |
          |
1100 <+  -+
   ^  |
1101  |
      |
1110 -+
   ^
1111
```

Do you see this pattern? We take the elements in the cell under *the index less than ours by some power of two and use it as a base* to create the current set by adding the current outermost element. What is this power of two? You can see from the same diagram that the number of current leading zeros (or it can be represented as number of first element), that is, the set of neighbors that can be used to create the current one, is located $2^{zeros}$ steps back, where $zeros$ is the number of current leading zeros of the iteration number.

Но ведь это таблица динамического программирования! Для вычисления текущего множества мы используем результат предыдущего вычисления. Стоп, и еще раз погодите! Не значит ли это, что для вычисления соседей для каждого из $2^i$ подмножества нам потребуется обработать ровно $2^i$ узлов? Да, значит! Таким образом все наше множество можно поделить на 2 части: базовую (base), где мы закешировали редко меняющуюся старшую часть, и табличную (table), которая вычисляется с помощью нашей таблицы динамического программирования. Вопроса о размере каждой части не возникает — table-часть берет то, что осталось: $64 - len(MSB)$ (либо наоборот). По итогу мы имеем следующий алгоритм:

But this is a dynamic programming table! To calculate the current set, we use the result of the previous calculation. Wait! Doesn't this mean that to calculate the neighbors for each of the $2^i$ subset, we will need to process exactly $2^i$ nodes? Yes, it means! Thus, our entire set can be divided into 2 parts: the base part, where we have cached the rarely changing upper part, and the table part, which is calculated using our dynamic programming table. There is no question about the size of each part — the table part takes what is left: $64 - len(MSB)$ (or vice versa). As a result, we have the following algorithm:

1. If iteration number (number representation of subset bit mask) is divided by $2^{table size}$, then compute neighborhood - this is new base part begins.
2. Otherwise:
   1. Take lower part of iteration number (i.e. using bit mask)
   2. Calculate number of zeros in subset and get delta: $delta = 2^{zeros}$
   3. Take parent neighborhood (starting point for computation): $table[iteration - delta]$
   4. Compute neighborhood using first element
   5. Save computed neighborhood to table

A practical question arises: how much memory to allocate for table? An 8-byte number is used to store the set (`bitmapword == uint64`), so the size of each set is fixed. This means that in my case it takes $8* 2^i = 2^{i+3}$ bytes to store a table with $i$ elements. Now we can make rough estimates.

| Table size | Memory consumption | Total | Optimized | Saved |
| ---------- | ------------------ | ----- | --------- | ----- |
| 2          | 32   b             | 4     | 3         | 1     |
| 3          | 64   b             | 12    | 7         | 5     |
| 4          | 128  b             | 32    | 15        | 17    |
| 5          | 256  b             | 80    | 31        | 49    |
| 6          | 512  b             | 192   | 63        | 129   |
| 7          | 1    Kb            | 448   | 127       | 321   |
| 8          | 2    Kb            | 1024  | 255       | 769   |
| 9          | 4    Kb            | 2304  | 511       | 1793  |
| 10         | 8    Kb            | 5120  | 1023      | 4097  |
| 11         | 16   Kb            | 11264 | 2047      | 9217  |
| 12         | 32   Kb            | 24576 | 4095      | 20481 |

Legend:

- Table size — size of suffix we are caching.
- Memory consumption — amount of memory required to allocate table ($2^{table\ size + 3}$).
- Total — total number of all elements across all subsets ($\sum_{k = 1}^{table\ size}k C_{table\ size}^{k}$).
- Optimized — number of elements we have to process in table version, the same as number of iterations but without first (empty) set ($2^{table\ size} - 1$).
- Saved — difference between "Total" and "Optimized".

Which table size to use can be calculated dynamically or simply choose the optimal value for your load heuristically (constant set in configuration). To make it easier to think further, I will choose a table size of 10 elements.

<spoiler title="Increasing the table only reduces the number of iterations">

I was wondering — what kind of gain does an increase in the table give? For example, how many iterations can we save if we use `tbl + 1` instead of a table with `tbl` elements. To begin with, here is the formula for the total number of iterations. Let's imagine that the number of our neighbors (i.e., the size of the set) is `max` (for current implementation it is `64`, but this is generalization), and the size of the table is `tbl`. Then, the number of iterations required to process all subsets of neighbors is:

$$
\sum^{k = 0}_{max - tbl}C_{max - tbl}^{k}(2^{tbl} - 1 + k)
$$

That is, for each new `base` there are $k$ iterations to calculate its neighbors, and then another $2^{tbl}$ iterations for table. We do this for each subset ($\sum^{k}_{max - tbl}$). Now we can calculate the difference between the number of iterations for the current table and the one increased by 1.

$$
\sum_{k}^{max - tbl}C_{max - tbl}^{k}(2^{tbl} - 1 + k) - \sum_{k}^{max - tbl - 1}C_{max - tbl - 1}^{k}(2^{tbl + 1} - 1 + k) = \\
\sum_{k}^{max - tbl - 1}C_{max - tbl}^{k}(2^{tbl} - 1 + k) - \sum_{k}^{max - tbl - 1}C_{max - tbl - 1}^{k}(2^{tbl + 1} - 1 + k) + C_{max - tbl}^{max - tbl}(2^{tbl} - 1 + max - tbl) = \\
\sum_{k}^{max - tbl - 1}(C_{max - tbl - 1}^{k} + C_{max - tbl - 1}^{k - 1})(2^{tbl} - 1 + k) - \sum_{k}^{max - tbl - 1}C_{max - tbl - 1}^{k}(2^{tbl + 1} - 1 + k) + 2^{tbl} - 1 + max - tbl = \\
\sum_{k}^{max - tbl - 1}C_{max - tbl - 1}^{k}(2^{tbl} - 1 + k) + \sum_{k}^{max - tbl - 1}C_{max - tbl - 1}^{k - 1}(2^{tbl} - 1 + k) - \sum_{k}^{max - tbl - 1}C_{max - tbl - 1}^{k}(2^{tbl + 1} - 1 + k) + 2^{tbl} - 1 + max - tbl = \\
\sum_{k}^{max - tbl - 1}C_{max - tbl - 1}^{k}(2^{tbl} - 1 + k - 2^{tbl + 1} + 1 - k) + \sum_{k}^{max - tbl - 1}C_{max - tbl - 1}^{k - 1}(2^{tbl} - 1 + k) + 2^{tbl} - 1 + max - tbl = \\
-2^{tbl}\sum_{k}^{max - tbl - 1}C_{max - tbl - 1}^{k} + \sum_{k}^{max - tbl - 1}C_{max - tbl - 1}^{k - 1}(2 ^{tbl} - 1 + k) + 2^{tbl} - 1 + max - tbl = \\
2^{tbl}(\sum_{k}^{max - tbl - 1}C_{max - tbl - 1}^{k - 1} - \sum_{k}^{max - tbl - 1}C_{max - tbl - 1}^{k}) + \sum_{k}^{max - tbl - 1}C_{max - tbl - 1}^{k - 1}(2^{tbl} - 1 + k) + 2^{tbl} + max - tbl
$$

If we expand the sums under the brackets in the first term, we get a sum of the following type: $C^0 - C^1 + C^1 - C^2 + C^2 - ...$. It is easy to see that the terms destroy each other, and we get $C^0 - C^{max - tbl - 1} = 0$ — the first term is zero. Then the final expression is:

$$
\sum_{k}^{max - tbl - 1}C_{max - tbl - 1}^{k - 1}(2^{tbl} - 1 + k) + 2^{tbl} - 1 + max - tbl = \\
\sum_{k}^{max - tbl - 1}C_{max - tbl - 1}^{k - 1}(2^{tbl} - 1 + k) = \\
\sum_{k}^{max - tbl}C_{max - tbl - 1}^{k}(2^{tbl} + k)
$$

Considering that $max > tbl$ (the formula assumes that we increase $tbl$ by 1, that is, this is the previous value, and it makes no sense to increase the size of the table beyond the size of the set), we see that this expression is non-negative, since each term of this sum is non-negative.

</spoiler>

### 3-layered cache

So, we came up with the idea of storing a table of cached neighborhood. It's not bad, but it's sad that there are only fixed number of nodes in the cache. For complex queries, we will have to recalculate the base. Yes, we can make the table size configurable, but in any case, from some point on, the table size may become impractically large, i.e., starting with 15 elements, a *256 Kb* table will be required. In the worst case, after several similar recursive calls (for example, the same `EnumerateCsgRecursive`), the memory allocated only for tables may amount to more than a megabyte. Is there any other way to optimize? Yes.

Let's go back to the beginning. The whole idea of caching in MySQL was based on calculating the neighbors by the first node of the current set, but when the moment came to save the neighbors for further calculations, they *did not* save the neighbors if this first `taboo bit` was set. Why? But because this is the final station! Neighbors created in such set will no longer be used by anyone when iterating incrementally. It doesn't take long to go after the proof — look at the previous scheme and you'll see that the neighbors calculated in odd iterations are not used by anyone. That is, we can safely avoid saving neighbors of odd iterations. How much does this optimization save us? Exactly half: half of the sets are even, and the other half are odd. The code, of course, will have to be slightly modified, it is necessary to take into account the indexing of the new scheme: to get the previous index, you need to take not $2^{zeros}$, but $2^{zeros - 1}$ elements back.

Wait, but if we cut off the first bits, there will be another part with a similar pattern. What if you optimize it that way too, and how many times can you repeat it that way? I won't waste much time on these thoughts, but I'll say right away that I was able to apply a similar prefix compression for four elements, while only needing two additional variables to calculate the current neighbors. Thus, we come to a 3-layered caching scheme: base, table, hot (initially I called them well-done, medium and rare). The third part, hot, can be called a compressed prefix.

The algorithm now depends even more on the iteration number: depending on its value, we work with the base, table, or hot part.

First, let's look at the order of processing the hot part. It consists of 4 elements, and the main thing is that the pattern repeats itself:

```text
0   0000 <+  <+  <+    quad leader
       ^  |   |   |
1   0001  |   |   |
          |   |   |
2   0010 -+   |   |
       ^      |   |
3   0011      |   |
              |   |
4   0100 <+  -+   |
       ^  |       |
5   0101  |       |
          |       |
6   0110 -+       |
       ^          |
7   0111          |
                  |
8   1000 <+  <+  -+    quad leader
       ^  |   |
9   1001  |   |
          |   |
10  1010 -+   |
       ^      |
11  1011      |
              |
12  1100 <+  -+
       ^  |
13  1101  |
          |
14  1110 -+
       ^
15  1111
```

We divide all these 16 elements (total number of possible subsets for 4 elements) in half into 2 parts, each of which has its own leader, called the *quad leader*. In fact, these are the cached neighbors for a set in which only the last bit is set. We calculate it 2 times: at iterations 0 and 8. Next, we also use optimization for odd iterations — for them, we specifically save the neighbors of even iterations at each iteration and simply use the ready-made value at the next (already odd) iteration (we don't need to save anything for odd ones).

As a result, we are left with iterations: 2, 4, 6, 10, 12, 14. Here we can note that for 2, 6, 10, 14, we just need to take the neighbors of the previous even iteration — we save it for odd iterations anyway. But 4 and 12 require special processing: we need to take the quad leader as a starting point, but we also save it (the second required variable). As a result, we store only two calculated values of the neighbors: the previous even iteration and the quad leader.

Moving on to the table part. The changes primarily affected the calculation of the table index. Now, for the parent neighborhood, you need to subtract `4` (the size of the hot part) from the number of leading zeros: $2^{zeros - 4}$. It is worth noting that when a new table part begins, a new hot part also begins, so each time after calculating the neighbors and saving this value to the table, we should clear the state of the hot part - assign a new quad-leader with the current neighbor value.

There are almost no changes with the base part. First, as can be understood from the previous steps, when starting a new base part, we need to reset the state of the table part (the calculation of the table begins anew), as well as the hot part (set the quad leader to the current calculated value of the neighbors). But that's not all: why don't we use the same trick with odd iterations? And it's true: if the first bit of the current iteration number of the base part is 1, then we can simply recompute the neighbors, just as we did before.

The last question is: how to decide which action to perform? Use the subset prefix here. It is easy to see that:

- every $2^{table size}$ iterations, the base part should be updated (which in terms of binary numbers means that the first $i$ bits are 0);
- every $2^4 = 16$ iterations, new entries should be made in the table;
- every $8$ iterations, the quad leader should be updated in the hot part.

Алгоритм значительно усложнился, но чего не сделаешь ради оптимизаций. Зато теперь для того же размера множества требуется не $2^{i}$ байт, а всего лишь $2^{i - 4}$. Например, вместо 8Кб теперь требуется только 0.5Кб, но при этом кешируется все 10 элементов.

The algorithm has become much more complicated, but that is done for the sake of optimization. Now for the same set size, not $2^{i}$ bytes are required, but only $2^{i - 4}$. For example, instead of 8Kb, only 0.5Kb is now required whereas still all 10 elements are cached.

And when this idea settled in my head and I started writing code with all my might, it suddenly dawned on me...

### Perfect cache

I looked at the graph again, wrote it for five elements, and noticed something that was in plain sight all the time, but I didn't notice. Here is this diagram, on the right of which is the number of accesses to the elements (I did not write for odd ones):

```text
00000 <+  <+  <+  <+    5
    ^  |   |   |   |  
00001  |   |   |   |
       |   |   |   |
00010 -+   |   |   |    1
    ^      |   |   |
00011      |   |   |
           |   |   |
00100 <+  -+   |   |    2
    ^  |       |   |
00101  |       |   |
       |       |   |
00110 -+       |   |    1
    ^          |   |
00111          |   |
               |   |
01000 <+  <+  -+   |    3
    ^  |   |       |
01001  |   |       |
       |   |       |
01010 -+   |       |    1
    ^      |       |
01011      |       |
           |       |
01100 <+  -+       |    2
    ^  |           |
01101  |           |
       |           |
01110 -+           |    1
    ^              |
11111              |
                   |
10000 <+  <+  <+  -+    4  
    ^  |   |   |
10001  |   |   |
       |   |   |
10010 -+   |   |        1
    ^      |   |
10011      |   |
           |   |
10100 <+  -+   |        2
    ^  |       |
10101  |       |
       |       |
10110 -+       |        1
    ^          |
10111          |
               |
11000 <+  <+  -+        3
    ^  |   |
11001  |   |
       |   |
11010 -+   |            1
    ^      |
11011      |
           |
11100 <+  -+            2
    ^  |
11101  |
       |
11110 -+                1
    ^
11111
```

Do you see the pattern? If not yet, here's a hint — the relationship between the prefix of the iteration number and the number of hits. The number of accesses to the *element is equal to the number of zeros at the beginning, and after this number of accesses, the value  will not be used at all*. Instead, it will be a new one, but the iteration number will have the same number of zeros at the beginning. That is, we *don't need* to store these values, and since our iteration is only forward (incremental), we can safely delete old elements. With this approach, the maximum size of the table is equal to the size max size of nodes. Since I represent a set as a number, I don't even need to think about it — I allocate a 64-sized array on the stack (64 * 8 = 512 bytes).

But how to use it correctly? Let's remember how we compute the neighbors: we take the parent neighborhood and calculate current one with the first element. And here's the second observation: the parent set is ours, but without the last bit, for example, for `01110` the parent will be `01100`. And which cell the parent neighborhood for `01100` is stored? That's right — under the index `2`, because it has 2 zeros at the beginning. The corner case is when we move to a new digit, then after deleting a bit we get an empty set, but for an empty set (nodes) we return an empty set (neighborhood), because it cannot have neighbors.

And now the algorithm itself:

1. Create DP-table, (dynamic) array.
2. Start next iteration.
3. Remove the first bit (element) from the iteration number and count the number of zeros.
4. Take the element with this index from the DP table.
5. Calculate the neighborhood based on the cached one, and for delta — the first element of the current subset.
6. Save computed neighborhood to DP-table with index of current number of zeros.

Корректно ли это? Корректно. Вот доказательство от обратного. Базой будет то, что соседи пустого множества — это пустое множество (либо другое, но определенное значение). Само доказательство исходит из свойства алгоритма перебора подмножеств — инкремента.

Is this correct? Yes. Here is a evidence to the contrary. The proof itself comes from the property of the subset—increment algorithm.

Algorithm:

1. Given current iteration `iter`.
2. Remove last bit and get number `iter_parent`.
3. Calculate number of zeros and get parent neighborhood from table `nb_parent`.

It is stated that `nb_parent` is the neighborhood of the set `iter_parent`. Let's assume that it is not, that is, the neighbors of `iter_parent_2` are stored in that cell, which is not equal to `iter_parent`. Then this number can be either more or less than expected, but:

- `iter_parent_2` definitely can not be greater than `iter_parent`, since this means that it would be greater than the original `iter` (since the differences are in the MSB part), but we iterate in a strictly increasing way, which means that *we have not yet encountered this number*, and its value is not in the table.
- it can't be a smaller number either, because that would mean that we missed the `iter_parent`: both numbers have the same number of leading zeros (according to our assumption), and since `iter_parent` comes *after* `iter_parent_2` (since it is larger in numerical representation), we should have overwritten the value under the corresponding index (overwrite the neighbors of `iter_parent_2`), but we did not do this and thus violated the algorithm.

The only remaining option is that the neighborhood of the `iter_parent` is stored under this index. Given that the neighbors are defined for an empty set, we can also say that we can't get garbage either:

- if the current set consists of single element, it means that we have moved to a new higher order digit, which we have not been in before. By removing this element, we get an empty set, but the neighbors are defined for it, and for this new index, which has not been seen before, the neighbors will be preserved, and then the value will be determined;
- otherwise, the number of leading zeros will *not exceed* the number of the highest digit (otherwise we started a new digit and this is case 1), and we should have already written the neighbors for them in the table, that is, it is determined.

So we proved that this caching scheme is correct. Here is the code itself that calculates it all.:

```c++
typedef struct SubsetIteratorState
{
    /* Current subset */
    bitmapword subset;
    /* State variables for subset iteration */
    bitmapword state;
    bitmapword init;
    /* Current iteration number */
    bitmapword iteration;
    /* Neighborhood cache */
    bitmapword cached_neighborhood[64];
} SubsetIteratorState;

/* Get parent neighborhood */
static inline bitmapword
get_parent_neighborhood(DPHypContext *context, SubsetIteratorState *iter_state)
{
    int zero_count;
    bitmapword last_bit_removed;

    /* Remove first bit/element */
    last_bit_removed = bmw_difference(iter_state->iteration, bmw_lowest_bit(iter_state->iteration));
    if (last_bit_removed == 0)
    {
        /* There are no neighbors */
        return 0;
    }

    zero_count = bmw_rightmost_one_pos(last_bit_removed);
    return iter_state->cached_neighborhood[zero_count];
}

/* Calculate neighborhood for current iteration */
static bitmapword
get_neighbors_iter(DPHypContext *context, bitmapword subgroup,
                   bitmapword excluded, SubsetIteratorState *iter_state)
{
    int i;
    int idx;
    int zero_count;
    bitmapword neighbors;
    EdgeArray *complex_edges;

    excluded |= subgroup;

    iter_state->iteration++;

    idx = bmw_rightmost_one_pos(iter_state->subset);

    /* Computation basis - parent neighborhood */
    neighbors = get_parent_neighborhood(context, iter_state);

    /* Add simple neighborhood */
    neighbors |= bmw_difference(context->simple_edges[idx], excluded);

    /* Process complex edges */
    complex_edges = &context->complex_edges[idx];
    i = get_start_index(complex_edges, neighbors | excluded);
    for (; i < complex_edges->size; i++)
    {
        HyperEdge edge = complex_edges->edges[i];
        if ( bmw_is_subset(edge.left, subgroup) &&
            !bmw_overlap(edge.right, neighbors | excluded))
        {
            neighbors |= bmw_lowest_bit(edge.right);
        }
    }

    neighbors = bmw_difference(neighbors, excluded);

    /* Save current neighborhood to table */
    zero_count = bmw_rightmost_one_pos(iter_state->iteration);
    iter_state->cached_neighborhood[zero_count] = neighbors;

    return neighbors;
}
```

> The scheme can also be slightly optimized - do not save neighborhood for odd iterations, but this is micro-optimization.

Is it possible to add more optimizations? Yes. An example of this is already in the code above — indexing.

<spoiler title="Not exactly perfect cache">

Yes, this caching strategy is good, but not perfect. Remember the MySQL caching approach — they use it even in the first `EnumerateCsgRec` cycle, when excluded set varies. MySQL overcomes this using heuristic — it caches excluded nodes, and then add all previously prohibited ones which *could be* neighbors.

Here is an example when this is *not* the case. There are 3 calls to `EnumerateCsgRec` (`S` is a subgraph, `X` is a set of excluded nodes, `N` is the neighbors that are currently being iterated over, i.e. `N(S, X)` has been calculated):

```text
1. S = 00000001, X = 00000001, N = 00001110
2. S = 00001001, X = 00001111, N = 01110000
3. S = 01001001, X = 01111111, N = 10000000
```

The first two iterations yielded neighbors of three adjacent elements, and now we are in the third call: subgraph = `1001001`, excluded = `1111111`, neighbors = `10000000`. In the first (and only cycle), we found that `11001001` has a plan, so we need to find neighbors for this set. Following the logic of MySQL, the neighbors for it are `10110110`, since you need to add all the previous neighbors.

Where could there be a problem here? Look at the second call: where did the neighbors `1110000` come from? We got them from `0000100`, but it *was not in our **final** subgraph*, which means that one of the hyperedges could contain `01000000`, which should be excluded (contained in the final subgraph).

It is not difficult to guess that after this, the number of nodes (and therefore possible csg/cmp pairs) that should be considered will increase, which means that the number of "junk" solutions that either will not give a useful answer or will be considered several times will also increase. There is certainly a connection between them, since the past neighbors are obtained from current nodes.

It is difficult to say whether it is bad or good for MySQL. But for PostgreSQL, at least at this stage of the extension's life, we can say that it is bad. The reason is the choice of the plan creation function — `make_join_rel`. Calling this function is very expensive and takes up almost all the time (i.e. if you built flame-graph for planner). If we use the MySQL approach in this part, then as a result, quite a lot of unnecessary CSG/CMP pairs will be created, for which we will have to create a plan. Most likely, we will waste time and resources on this, because even if there are no predicates between the relations, we can create a `CROSS JOIN`. In short, in the current cost model, it is much more profitable to spend an extra call to the neighbor finding function (`get_neighbors`) than to create a plan (`make_join_rel`). But that's not all.

Note the fundamental difference in the caching approach. In the MySQL approach, they can quite rightly use their cache and use it if necessary, since you just need to check the subset. Maybe it's not as optimal, but it works even in the case of *conditional* execution. But the "perfect" cache is another matter, it requires us to constantly maintain it, that is, we are required to compute the neighbors at each iteration to maintain DP-table intact so that further calculations give the correct result.

It's hard to say which is better.:

1. In some scenarios, it makes sense to use an "perfect" cache (and adapt caching from MySQL):
   - we already have a plan in place for most of the subsets;
   - no further recursive calls are expected (and there will be no combinatorial explosion)
2. In other scenarios, there are no such plans at all, so there is no need to compute neighbors, you can only conditionally calculate them.

Even so, taking the first approach, we should evaluate the consequences, since adding a few more nodes to the neighborhood will increase further costs for other iterations. This can almost completely negate the gains you're making right now: don't forget that each additional node increases the number of subset iterations *by 2 times*, and this additional node will also add new nodes to future neighbors! From the example above, we added two nodes to the neighbors, which means that there will be four times as many iterations, and for each one we will need to find more neighbors, call other functions, and so on. And how many such "indirect" neighbors will accumulate for an even greater number of recursive calls is hard to imagine.

</spoiler>

## Индексирование ребер

Рассказывая о деталях реализации, я упомянул, что сложные ребра сортируются, это помогает избавиться от дубликатов, но есть еще одна польза. Если мы взглянем на некоторые части DPhyp, а конкретно на места, в которых нам необходимо обходить ребра — нахождение соседей и определение связи (ребра) между двумя гиперузлами, — то можно заметить похожий паттерн с идеей кеширования: есть движущиеся детали, а есть постоянные:

- при поиске соседей множество исключенных узлов (`excluded`) неизменно (либо только увеличивается);
- при определении связи между гиперузлами оба гиперузла фиксированы.

Попробуем как-то использовать это знание, и для начала научимся учитывать исключенные узлы. Анализ DPhyp дает понять, что множество исключенных узлов имеет одинаковую структуру: лидирующие единицы, а затем разрозненные элементы, например, `010110011111` — в нем есть пять лидирующих единиц. Это неслучайно, алгоритм так работает: мы не должны смотреть на узлы, которые еще не обрабатывали, поэтому каждый раз при обходе ребер мы проверяем, что никакая часть с этими исключенными не пересекается. Множество исключенных узлов в процессе итерирования может только увеличиться, но не уменьшиться, а это значит, что мы заранее можем знать, какие узлы точно не будут удовлетворять условию, то есть будут пересекаться с этими лидирующими нулями.

Можем сделать вот что — взять все гиперребра и отсортировать их по правой части, а для сравнения использовать количество лидирующих нулей. Затем, во время итерирования, мы вычисляем длину последовательности лидирующих единиц во множестве исключенных и стартовый индекс для итерации ставим таким, чтобы первый элемент правой части ребра точно превышал последний элемент последовательности единиц.

Покажу на примере. У нас имеется несколько отсортированных по количеству лидирующих нулей гиперребер. Справа написал индексы для удобства (левая часть гиперребра не важна, только правая):

```text
[
    xxxxx - 00101    0
    xxxxx - 00111    1
    xxxxx - 00100    2
    xxxxx - 00100    3
    xxxxx - 01100    4
    xxxxx - 10100    5
    xxxxx - 11100    6
    xxxxx - 01000    7
]
```

Теперь легко понять, с какого индекса нам стоит начать итерацию в зависимости от того, какое перед нами множество исключенных узлов:

| `excluded` | стартовый индекс |
| ---------- | ---------------- |
| `00100`    | 0                |
| `00001`    | 2                |
| `11101`    | 2                |
| `10011`    | 2                |
| `00111`    | 7                |
| `01111`    | 8                |

Несколько моментов:

1. Мы можем быть уверены только в размере лидирующих единиц, остальная часть для нас — черный ящик. Например, из-за этого в третьем примере итерацию начинаем со второго индекса, хотя видно, что остальная часть полностью исключена.
2. Для обобщения кода в случае когда нет подходящих ребер мы можем возвращать длину массива (большое число).

Чтобы понять пользу этой оптимизации, вспомните, что все ребра в графе — двунаправленные, то есть для каждого гиперребра мы создаем две пары с обменянными левой и правой частями. Может оказаться, что на какой-то узел ссылается очень большое количество таблиц (например, это таблица фактов, а размерностей у нас 100+), а сама эта таблица имеет самый большой индекс, то есть `excluded` будет включать почти все таблицы. В этом случае нам придется безуспешно проходить по всем сложным ребрам, зная, что это не даст никакого результата.

Хорошо, а как мы будем получать стартовый индекс? Первое что приходит в голову, когда говорят "отсортированный" — бинарный поиск, но не спешите с выводами. Вспомним, что наше пространство "ключей" (это количество лидирующих нулей/единиц) — дискретное. Начинается оно с нуля и может только увеличиваться (в моем случае есть предел — 64). А что подходит под описание такой структуры? Массив. Под индексом `i` будет храниться первый индекс массива гиперребер, первый элемент правой части (гиперребра) которого не меньше этого `i`.

А что делать с пробелами? В примере после `00111` сразу идет `00100`, без `00010`. Эти пробелы мы заполняем предыдущим значением, то есть если для какой-то длины `i` я не знаю, с какого индекса мне лучше начать итерацию, то нужно взять предыдущий индекс, так как если мой первый элемент `i`, то я буду удовлетворять и всем последовательностям исключенных длиной меньше этого `i`. За базу мы возьмем индекс `0`, то есть если такой последовательности исключенных узлов нет, то ее значение будет `0`, так как в этом случае мы должны проитерироваться по всем гиперребрам.

Затраты на использование такого индекса равны только расчету количества лидирующих единиц во множестве исключенных узлов. Но это можно оптимизировать простым битовым трюком — прибавляем `1` и получаем последовательность из `0` такой же длины, но оканчивающуюся на `1`, а затем рассчитываем позицию этой единицы. Получить это значение можно специальной инструкцией, например, `POPCNT`.

Для ребер из примера мы можем построить такой индекс (индекс массива ребер справа):

```text
[
    0: 0
    1: 2
    2: 2
    3: 7
    4: 8
    5: 8
]
```

Размер этого индекса зависит только от максимального количества лидирующих `0` во всем массиве ребер. Для примера видно, что я мог бы создать массив только из четырех элементов, а дальше всегда возвращать `8` (как размер массива).

Мы научились быстро отсекать ненужные ребра при поиске соседей. Но по ребрам мы ходим также для определения связанности двух гиперузлов. Можно ли использовать этот индекс и здесь? Да, можно. Два гиперузла соединены, когда есть гиперребро, левая часть которого — подмножество левого гиперузла, а правая часть — правого. Гиперузлы на входе никак не меняются, точно так же, как и множество исключенных. А теперь надо заметить, что правая часть гиперребра точно не будет подмножеством, если в этой части есть элементы, индекс которых меньше, чем наименьший в гиперузле справа. Пример: правая часть гиперребра `001010` никак не будет подмножеством гиперузла `001100`, поскольку в ребре есть элемент с индексом 2. То есть семантика здесь практически та же, что и в случае исключенного множества, — мы должны исключать все ребра, у которых есть элементы меньше наименьшего элемента правого гиперузла.

Вычисление стартового индекса находится в функции `get_start_index`. Для удобства на вход она принимает не готовый индекс, а множество исключенных узлов. Эта функция также используется при определении связанности двух гиперузлов: надо просто перед передачей аргумента из множества, представляющего правое гиперребро, вычесть единицу, тогда последовательность нулей обратно станет последовательностью единиц, что по требуемой семантике — одно и то же.

```c++
static int
get_start_index(EdgeArray *edges, bitmapword excluded)
{
    int index;
    int lowest_bit;

    lowest_bit = bmw_rightmost_one_pos(excluded + 1);

    if (edges->start_idx_size <= lowest_bit)
        return edges->size;

    index = (int)edges->start_idx[lowest_bit];
    return index;
}
```

## Определение сложности запроса

Итак, PostgreSQL использует 2 алгоритма: DPsize и GEQO, причем последний используется, если в запросе таблиц больше, чем значение параметра `geqo_threshold`. Но почему именно количество таблиц? Дело в том, что сложность DPsize определяется количеством таблиц — мы безусловно будем рассматривать все возможные комбинации. Но с DPhyp все обстоит иначе, его сложность зависит от самого графа запроса. В оригинальной статье сравниваются производительности алгоритмов на некоторых типах запросов, но они не дают прямого ответа на то, как определять сложность запроса (а может, я проглядел). Этот ответ приводится в другой статье — ["Adaptive Optimization of Large Join Queries"](https://db.in.tum.de/~radke/papers/hugejoins.pdf).

Автор этой статьи предлагает метаалгоритм, который, комбинируя несколько разных алгоритмов JOIN'а, позволяет создать планы запросов с количеством таблиц в несколько тысяч. В простых запросах авторы предлагают использовать DPhyp, но что такое простой запрос? Например, если в запросе 100 таблиц, то это не значит, что DPhyp с ним не справится. Если граф запроса представляет простую цепочку, например, все предикаты в форме `Ti.x OP T(i + 1).x`, то план для него найти несложно, но вот если это клика (каждый с каждым), то даже 15 таблиц — это уже много. Для DPhyp сложность надо определять не в количестве таблиц, а в сложности графа запроса — **количестве связных подграфов**. Значение в 10000 связных подграфов — как предел эффективности запроса, это соответствует примерно 14 таблицам в клике.

В этой же самой статье не только предлагается идея, но также и функция для вычисления количества связных подграфов. Посмотрев на нее, я понял: она идеально ложится на схему кеширования, описанную выше. Вот эта функция:

```c++
static uint64
count_cc_recursive(DPHypContext *context, bitmapword subgraph, bitmapword excluded,
                   uint64 count, uint64 max, bitmapword base_neighborhood)
{
    SubsetIteratorState subset_iter;
    subset_iterator_init(&subset_iter, base_neighborhood);
    while (subset_iterator_next(&subset_iter))
    {
        bitmapword set;
        bitmapword excluded_ext;
        bitmapword neighborhood;
  
        count++;
        if (count > max)
            break;

        excluded_ext = excluded | base_neighborhood;
        set = subgraph | subset_iter.subset;
        neighborhood = get_neighbors_iter(context, set, excluded_ext, &subset_iter);
        count = count_cc_recursive(context, set, excluded_ext, count, max, neighborhood);
    }

    return count;
}

static uint64
count_cc(DPHypContext *context, uint64 max)
{
    int64 count = 0;
    int rels_count;

    rels_count = list_length(context->initial_rels);
    for (size_t i = 0; i < rels_count; i++)
    {
        bitmapword excluded;
        bitmapword neighborhood;

        count++;
        if (count > max)
            break;

        excluded = bmw_make_b_v(i);
        neighborhood = get_neighbors_base(context, i, excluded);
        count = count_cc_recursive(context, bmw_make_singleton(i), excluded,
                                   count, max, neighborhood);
    }

    return count;
}
```

Названия функций я оставил теми же, что и в статье, но адаптировал сигнатуру для поддержки эффективного итерирования по соседям.

Бонус: количество связных подграфов — это размер результирующей DP-таблицы. Сейчас это значение используется для ее предварительной аллокации.

## Тестирование

Проверим все вначале на каком-нибудь простом запросе:

```sql
EXPLAIN ANALYZE
SELECT * 
FROM t1, t2, t3, t4, t5, t6, t7, t8, t9, t10, t11, t12, t13, t14
WHERE 
    t1.x + t2.x + t3.x + t4.x + t5.x + t6.x + t7.x > t8.x + t9.x + t10.x + t11.x + t12.x + t13.x + t14.x;
```

Это 14 таблиц, соединенных одним гиперребром: `{t1, t2, t3, t4, t5, t6, t7} - {t8, t9, t10, t11, t12, t13, t14}`. Каждая таблица — одноколоночная, с тремя значениями (чтобы запрос долго не выполнялся). Для DPsize результат следующий:

```text
                                                                  QUERY PLAN                                   
-----------------------------------------------------------------------------------------------------------------------------------------------
 Nested Loop  (cost=0.00..215311.78 rows=1594323 width=56) (actual time=1.672..1330.944 rows=2083371 loops=1)
   Join Filter: (((((((t1.x + t2.x) + t3.x) + t4.x) + t5.x) + t6.x) + t7.x) > ((((((t8.x + t9.x) + t10.x) + t11.x) + t12.x) + t13.x) + t14.x))
   Rows Removed by Join Filter: 2699598
   ->  Nested Loop  (cost=0.00..36.36 rows=2187 width=28) (actual time=0.057..0.612 rows=2187 loops=1)
         ->  Nested Loop  (cost=0.00..5.39 rows=81 width=16) (actual time=0.042..0.128 rows=81 loops=1)
               ->  Nested Loop  (cost=0.00..2.18 rows=9 width=8) (actual time=0.029..0.061 rows=9 loops=1)
                     ->  Seq Scan on t4  (cost=0.00..1.03 rows=3 width=4) (actual time=0.018..0.028 rows=3 loops=1)
                     ->  Materialize  (cost=0.00..1.04 rows=3 width=4) (actual time=0.003..0.008 rows=3 loops=3)
                           ->  Seq Scan on t5  (cost=0.00..1.03 rows=3 width=4) (actual time=0.003..0.012 rows=3 loops=1)
               ->  Materialize  (cost=0.00..2.23 rows=9 width=8) (actual time=0.001..0.005 rows=9 loops=9)
                     ->  Nested Loop  (cost=0.00..2.18 rows=9 width=8) (actual time=0.007..0.025 rows=9 loops=1)
                           ->  Seq Scan on t6  (cost=0.00..1.03 rows=3 width=4) (actual time=0.002..0.010 rows=3 loops=1)
                           ->  Materialize  (cost=0.00..1.04 rows=3 width=4) (actual time=0.002..0.004 rows=3 loops=3)
                                 ->  Seq Scan on t7  (cost=0.00..1.03 rows=3 width=4) (actual time=0.002..0.006 rows=3 loops=1)
         ->  Materialize  (cost=0.00..3.69 rows=27 width=12) (actual time=0.001..0.002 rows=27 loops=81)
               ->  Nested Loop  (cost=0.00..3.56 rows=27 width=12) (actual time=0.014..0.037 rows=27 loops=1)
                     ->  Nested Loop  (cost=0.00..2.18 rows=9 width=8) (actual time=0.008..0.022 rows=9 loops=1)
                           ->  Seq Scan on t1  (cost=0.00..1.03 rows=3 width=4) (actual time=0.003..0.008 rows=3 loops=1)
                           ->  Materialize  (cost=0.00..1.04 rows=3 width=4) (actual time=0.002..0.004 rows=3 loops=3)
                                 ->  Seq Scan on t2  (cost=0.00..1.03 rows=3 width=4) (actual time=0.002..0.007 rows=3 loops=1)
                     ->  Materialize  (cost=0.00..1.04 rows=3 width=4) (actual time=0.001..0.001 rows=3 loops=9)
                           ->  Seq Scan on t3  (cost=0.00..1.03 rows=3 width=4) (actual time=0.003..0.004 rows=3 loops=1)
   ->  Materialize  (cost=0.00..47.29 rows=2187 width=28) (actual time=0.000..0.078 rows=2187 loops=2187)
         ->  Nested Loop  (cost=0.00..36.36 rows=2187 width=28) (actual time=0.039..0.405 rows=2187 loops=1)
               ->  Nested Loop  (cost=0.00..5.39 rows=81 width=16) (actual time=0.021..0.043 rows=81 loops=1)
                     ->  Nested Loop  (cost=0.00..2.18 rows=9 width=8) (actual time=0.010..0.014 rows=9 loops=1)
                           ->  Seq Scan on t11  (cost=0.00..1.03 rows=3 width=4) (actual time=0.004..0.005 rows=3 loops=1)
                           ->  Materialize  (cost=0.00..1.04 rows=3 width=4) (actual time=0.002..0.002 rows=3 loops=3)
                                 ->  Seq Scan on t12  (cost=0.00..1.03 rows=3 width=4) (actual time=0.003..0.004 rows=3 loops=1)
                     ->  Materialize  (cost=0.00..2.23 rows=9 width=8) (actual time=0.001..0.002 rows=9 loops=9)
                           ->  Nested Loop  (cost=0.00..2.18 rows=9 width=8) (actual time=0.009..0.013 rows=9 loops=1)
                                 ->  Seq Scan on t13  (cost=0.00..1.03 rows=3 width=4) (actual time=0.004..0.005 rows=3 loops=1)
                                 ->  Materialize  (cost=0.00..1.04 rows=3 width=4) (actual time=0.001..0.002 rows=3 loops=3)
                                       ->  Seq Scan on t14  (cost=0.00..1.03 rows=3 width=4) (actual time=0.003..0.003 rows=3 loops=1)
               ->  Materialize  (cost=0.00..3.69 rows=27 width=12) (actual time=0.000..0.001 rows=27 loops=81)
                     ->  Nested Loop  (cost=0.00..3.56 rows=27 width=12) (actual time=0.016..0.026 rows=27 loops=1)
                           ->  Nested Loop  (cost=0.00..2.18 rows=9 width=8) (actual time=0.010..0.014 rows=9 loops=1)
                                 ->  Seq Scan on t8  (cost=0.00..1.03 rows=3 width=4) (actual time=0.005..0.006 rows=3 loops=1)
                                 ->  Materialize  (cost=0.00..1.04 rows=3 width=4) (actual time=0.001..0.002 rows=3 loops=3)
                                       ->  Seq Scan on t9  (cost=0.00..1.03 rows=3 width=4) (actual time=0.003..0.003 rows=3 loops=1)
                           ->  Materialize  (cost=0.00..1.04 rows=3 width=4) (actual time=0.001..0.001 rows=3 loops=9)
                                 ->  Seq Scan on t10  (cost=0.00..1.03 rows=3 width=4) (actual time=0.004..0.005 rows=3 loops=1)
 Planning Time: 3069.835 ms
 Execution Time: 1371.090 ms
(44 rows)
```

На планирование ушло 3 секунды, хотя на выполнение — меньше 1.5 секунд. Что нам даст DPhyp:

```text
                                                                  QUERY PLAN                                   
-----------------------------------------------------------------------------------------------------------------------------------------------
 Nested Loop  (cost=0.00..215311.78 rows=1594323 width=56) (actual time=1.612..1325.670 rows=2083371 loops=1)
   Join Filter: (((((((t1.x + t2.x) + t3.x) + t4.x) + t5.x) + t6.x) + t7.x) > ((((((t8.x + t9.x) + t10.x) + t11.x) + t12.x) + t13.x) + t14.x))
   Rows Removed by Join Filter: 2699598
   ->  Nested Loop  (cost=0.00..36.36 rows=2187 width=28) (actual time=0.039..0.551 rows=2187 loops=1)
         ->  Nested Loop  (cost=0.00..5.39 rows=81 width=16) (actual time=0.029..0.131 rows=81 loops=1)
               ->  Nested Loop  (cost=0.00..2.18 rows=9 width=8) (actual time=0.021..0.051 rows=9 loops=1)
                     ->  Seq Scan on t4  (cost=0.00..1.03 rows=3 width=4) (actual time=0.015..0.023 rows=3 loops=1)
                     ->  Materialize  (cost=0.00..1.04 rows=3 width=4) (actual time=0.001..0.006 rows=3 loops=3)
                           ->  Seq Scan on t5  (cost=0.00..1.03 rows=3 width=4) (actual time=0.003..0.012 rows=3 loops=1)
               ->  Materialize  (cost=0.00..2.23 rows=9 width=8) (actual time=0.001..0.006 rows=9 loops=9)
                     ->  Nested Loop  (cost=0.00..2.18 rows=9 width=8) (actual time=0.006..0.034 rows=9 loops=1)
                           ->  Seq Scan on t6  (cost=0.00..1.03 rows=3 width=4) (actual time=0.003..0.012 rows=3 loops=1)
                           ->  Materialize  (cost=0.00..1.04 rows=3 width=4) (actual time=0.001..0.005 rows=3 loops=3)
                                 ->  Seq Scan on t7  (cost=0.00..1.03 rows=3 width=4) (actual time=0.002..0.009 rows=3 loops=1)
         ->  Materialize  (cost=0.00..3.69 rows=27 width=12) (actual time=0.000..0.002 rows=27 loops=81)
               ->  Nested Loop  (cost=0.00..3.56 rows=27 width=12) (actual time=0.009..0.032 rows=27 loops=1)
                     ->  Nested Loop  (cost=0.00..2.18 rows=9 width=8) (actual time=0.006..0.020 rows=9 loops=1)
                           ->  Seq Scan on t2  (cost=0.00..1.03 rows=3 width=4) (actual time=0.002..0.008 rows=3 loops=1)
                           ->  Materialize  (cost=0.00..1.04 rows=3 width=4) (actual time=0.001..0.003 rows=3 loops=3)
                                 ->  Seq Scan on t3  (cost=0.00..1.03 rows=3 width=4) (actual time=0.002..0.006 rows=3 loops=1)
                     ->  Materialize  (cost=0.00..1.04 rows=3 width=4) (actual time=0.000..0.001 rows=3 loops=9)
                           ->  Seq Scan on t1  (cost=0.00..1.03 rows=3 width=4) (actual time=0.002..0.003 rows=3 loops=1)
   ->  Materialize  (cost=0.00..47.29 rows=2187 width=28) (actual time=0.000..0.078 rows=2187 loops=2187)
         ->  Nested Loop  (cost=0.00..36.36 rows=2187 width=28) (actual time=0.025..0.397 rows=2187 loops=1)
               ->  Nested Loop  (cost=0.00..5.39 rows=81 width=16) (actual time=0.013..0.037 rows=81 loops=1)
                     ->  Nested Loop  (cost=0.00..2.18 rows=9 width=8) (actual time=0.007..0.012 rows=9 loops=1)
                           ->  Seq Scan on t11  (cost=0.00..1.03 rows=3 width=4) (actual time=0.003..0.005 rows=3 loops=1)
                           ->  Materialize  (cost=0.00..1.04 rows=3 width=4) (actual time=0.001..0.002 rows=3 loops=3)
                                 ->  Seq Scan on t12  (cost=0.00..1.03 rows=3 width=4) (actual time=0.003..0.003 rows=3 loops=1)
                     ->  Materialize  (cost=0.00..2.23 rows=9 width=8) (actual time=0.001..0.002 rows=9 loops=9)
                           ->  Nested Loop  (cost=0.00..2.18 rows=9 width=8) (actual time=0.006..0.009 rows=9 loops=1)
                                 ->  Seq Scan on t13  (cost=0.00..1.03 rows=3 width=4) (actual time=0.003..0.003 rows=3 loops=1)
                                 ->  Materialize  (cost=0.00..1.04 rows=3 width=4) (actual time=0.001..0.001 rows=3 loops=3)
                                       ->  Seq Scan on t14  (cost=0.00..1.03 rows=3 width=4) (actual time=0.002..0.003 rows=3 loops=1)
               ->  Materialize  (cost=0.00..3.69 rows=27 width=12) (actual time=0.000..0.001 rows=27 loops=81)
                     ->  Nested Loop  (cost=0.00..3.56 rows=27 width=12) (actual time=0.011..0.021 rows=27 loops=1)
                           ->  Nested Loop  (cost=0.00..2.18 rows=9 width=8) (actual time=0.007..0.011 rows=9 loops=1)
                                 ->  Seq Scan on t9  (cost=0.00..1.03 rows=3 width=4) (actual time=0.004..0.004 rows=3 loops=1)
                                 ->  Materialize  (cost=0.00..1.04 rows=3 width=4) (actual time=0.001..0.001 rows=3 loops=3)
                                       ->  Seq Scan on t10  (cost=0.00..1.03 rows=3 width=4) (actual time=0.003..0.003 rows=3 loops=1)
                           ->  Materialize  (cost=0.00..1.04 rows=3 width=4) (actual time=0.000..0.001 rows=3 loops=9)
                                 ->  Seq Scan on t8  (cost=0.00..1.03 rows=3 width=4) (actual time=0.003..0.004 rows=3 loops=1)
 Planning Time: 4.706 ms
 Execution Time: 1365.543 ms
(44 rows)
```

**4 миллисекунды**! При этом планы идентичные — прирост производительности почти в **600 раз**!

Но это всего лишь небольшой запрос, давайте теперь возьмем что-то посолиднее. Возьмем к примеру готовый JOB (Join Order Benchmark), представленный в работе ["How Good Are Query Optimizers, Really?"](https://vldb.org/pvldb/vol9/p204-leis.pdf) и использованный для сравнения планировщиков нескольких СУБД, включая сам PostgreSQL. Бенчмарк можно найти в [этом репозитории](https://github.com/gregrahn/join-order-benchmark).

> В самой работе производилось сравнение не скорости работы планировщика, а оценок, которые он дает. Использовались несложные запросы — это INNER equi-JOIN'ы (т. е. условия JOIN — только равенство), однако для оценки реальной производительности следует использовать самые различные нагрузки, включая всякие сложные `LEFT JOIN`, но пока этого достаточно.

Как производилось тестирование:

- замер времени производился с помощью простенького расширения, которое замеряло время выполнения `join_search_hook`;
- после заливки данных итоговый размер БД — чуть больше 8Гб;
- всего имелось 113 запросов, но на самом деле их 33, все остальные суть вариации с различными константами. Для тестов каждый запрос запускался десять раз и рассчитывалось среднее время его выполнения, а затем, чтобы "шуметь", результаты одних и тех же классов запросов (с разными константами) группировались и рассчитывалось среднее значение.

По итогу всех запусков получилась такая таблица:

| Класс запроса | Время, DPsize | Время, DPhyp | Стоимость, DPsize    | Стоимость, DPhyp     |
| ------------- | ------------- | ------------ | -------------------- | -------------------- |
| 1             | 0.00          | 0.00         | 20063.63..20063.64   | 20047.21..20047.22   |
| 2             | 0.00          | 0.00         | 3917.21..3917.22     | 3865.78..3865.79     |
| 3             | 0.00          | 0.00         | 16893.51..16893.52   | 16893.07..16893.08   |
| 4             | 0.00          | 0.00         | 16537.03..16537.04   | 16532.75..16532.76   |
| 5             | 0.00          | 0.00         | 55136.70..55136.71   | 55110.84..55110.85   |
| 6             | 0.00          | 0.00         | 9136.23..9136.24     | 8601.80..8601.81     |
| 7             | 1.30          | 2.00         | 26596.24..26596.25   | 25281.84..25281.85   |
| 8             | 0.25          | 0.85         | 237500.71..237500.73 | 215342.88..215342.89 |
| 9             | 1.75          | 1.95         | 121709.02..121709.03 | 118041.88..118041.89 |
| 10            | 0.23          | 0.30         | 218646.80..218646.81 | 216146.76..216146.77 |
| 11            | 1.25          | 2.03         | 4264.53..4264.54     | 4263.81..4263.82     |
| 12            | 1.47          | 2.27         | 18062.07..18062.08   | 17923.86..17923.87   |
| 13            | 3.28          | 3.73         | 19880.57..19880.58   | 19561.58..19561.60   |
| 14            | 1.20          | 1.30         | 6675.30..6675.31     | 6675.12..6675.13     |
| 15            | 4.70          | 6.03         | 140612.22..140612.23 | 105280.24..105280.26 |
| 16            | 2.03          | 2.30         | 4373.61..4373.62     | 3928.40..3928.41     |
| 17            | 0.72          | 1.10         | 4526.53..4526.54     | 4073.57..4073.58     |
| 18            | 0.50          | 1.03         | 36882.96..36882.97   | 33151.67..33151.68   |
| 19            | 8.35          | 9.23         | 141225.66..141225.67 | 131527.60..131527.61 |
| 20            | 4.60          | 6.23         | 12982.67..12982.68   | 12976.69..12976.70   |
| 21            | 4.57          | 5.90         | 3833.12..3833.13     | 3833.12..3833.13     |
| 22            | 17.55         | 21.00        | 7532.63..7532.64     | 7532.28..7532.29     |
| 23            | 16.07         | 20.53        | 43981.68..43981.69   | 42108.50..42108.51   |
| 24            | 47.90         | 56.35        | 6665.46..6665.47     | 6580.84..6580.85     |
| 25            | 3.73          | 5.30         | 8502.14..8502.15     | 8495.15..8495.16     |
| 26            | 29.83         | 41.83        | 9324.37..9324.38     | 9237.43..9237.44     |
| 27            | 42.40         | 69.67        | 1053.48..1053.49     | 1290.69..1290.70     |
| 28            | 137.20        | 208.17       | 7534.70..7534.71     | 7534.67..7534.68     |
| 29            | 949.77        | 936.80       | 4013.73..4013.74     | 4013.73..4013.74     |
| 30            | 38.67         | 61.60        | 9323.59..9323.60     | 9323.59..9323.60     |
| 31            | 20.83         | 34.00        | 9584.13..9584.14     | 9575.61..9575.62     |
| 32            | 0.00          | 0.00         | 3880.96..3880.97     | 3838.37..3838.38     |
| 33            | 165.90        | 216.83       | 3029.91..3029.92     | 2995.14..2995.15     |

Когда я слегка пробежался по таблице, то не поверил своим глазам. Давайте посмотрим в виде графика:

![Наглядное сравнение получившихся стоимостей](https://raw.githubusercontent.com/ashenBlade/habr-posts/master/pg_dphyp/img/job_cost_compare.svg)

DPhyp в подавляющем большинстве создает план *лучше*, чем DPsize. Да, время его выполнения больше, но зато план-то лучше, а значит в долгосроке мы выигрываем!

Однако что-то здесь явно не так. Как минимум, мы только управляли *порядком* обследуемых отношений, но сами планы-то не создавали, это на самом PostgreSQL. Так что же, встроенный планировщик сломан? Для чистоты эксперимента давайте посмотрим, что получается на выходе. "Под нож" пойдет запрос `6f` (попался под руку). Вот что нам дает DPsize:

```text
                                                                        QUERY PLAN                                           
-----------------------------------------------------------------------------------------------------------------------------------------------------------
 Aggregate  (cost=15215.38..15215.39 rows=1 width=96)
   ->  Nested Loop  (cost=8.10..15175.34 rows=5339 width=48)
         ->  Nested Loop  (cost=7.67..12744.50 rows=5339 width=37)
               Join Filter: (ci.movie_id = t.id)
               ->  Nested Loop  (cost=7.23..12483.46 rows=147 width=41)
                     ->  Nested Loop  (cost=6.80..12351.21 rows=270 width=20)
                           ->  Seq Scan on keyword k  (cost=0.00..3691.40 rows=8 width=20)
                                 Filter: (keyword = ANY ('{superhero,sequel,second-part,marvel-comics,based-on-comic,tv-special,fight,violence}'::text[]))
                           ->  Bitmap Heap Scan on movie_keyword mk  (cost=6.80..1079.43 rows=305 width=8)
                                 Recheck Cond: (k.id = keyword_id)
                                 ->  Bitmap Index Scan on keyword_id_movie_keyword  (cost=0.00..6.72 rows=305 width=0)
                                       Index Cond: (keyword_id = k.id)
                     ->  Index Scan using title_pkey on title t  (cost=0.43..0.49 rows=1 width=21)
                           Index Cond: (id = mk.movie_id)
                           Filter: (production_year > 2000)
               ->  Index Scan using movie_id_cast_info on cast_info ci  (cost=0.44..1.33 rows=36 width=8)
                     Index Cond: (movie_id = mk.movie_id)
         ->  Index Scan using name_pkey on name n  (cost=0.43..0.46 rows=1 width=19)
               Index Cond: (id = ci.person_id)
(19 rows)
```

А вот что дает DPhyp:

```text
                                                                        QUERY PLAN                                         
-----------------------------------------------------------------------------------------------------------------------------------------------------------
 Aggregate  (cost=13720.08..13720.09 rows=1 width=96)
   ->  Nested Loop  (cost=8.10..13704.27 rows=2108 width=48)
         ->  Nested Loop  (cost=7.67..12744.50 rows=2108 width=37)
               Join Filter: (ci.movie_id = t.id)
               ->  Nested Loop  (cost=7.23..12483.46 rows=147 width=41)
                     ->  Nested Loop  (cost=6.80..12351.21 rows=270 width=20)
                           ->  Seq Scan on keyword k  (cost=0.00..3691.40 rows=8 width=20)
                                 Filter: (keyword = ANY ('{superhero,sequel,second-part,marvel-comics,based-on-comic,tv-special,fight,violence}'::text[]))
                           ->  Bitmap Heap Scan on movie_keyword mk  (cost=6.80..1079.43 rows=305 width=8)
                                 Recheck Cond: (k.id = keyword_id)
                                 ->  Bitmap Index Scan on keyword_id_movie_keyword  (cost=0.00..6.72 rows=305 width=0)
                                       Index Cond: (keyword_id = k.id)
                     ->  Index Scan using title_pkey on title t  (cost=0.43..0.49 rows=1 width=21)
                           Index Cond: (id = mk.movie_id)
                           Filter: (production_year > 2000)
               ->  Index Scan using movie_id_cast_info on cast_info ci  (cost=0.44..1.33 rows=36 width=8)
                     Index Cond: (movie_id = mk.movie_id)
         ->  Index Scan using name_pkey on name n  (cost=0.43..0.46 rows=1 width=19)
               Index Cond: (id = ci.person_id)
(19 rows)
```

План дешевле на почти на 20000 у.е.! То есть расширение действительно дает план лучше... Но стоп! **планы идентичные, но стоимости разные**? Различия наступают в третьем узле — `Nested Loop` с `ci.movie_id = t.id` предикатом: DPsize оценивает его в 5339 строк, а DPhyp — в 2108. Как такое вообще могло произойти? Надо найти причину.

Стартовой точкой будет трейсинг запроса, чтобы обнаружить, какие подпланы используются для создания этого NL. Придется делать это отладкой вручную, ведь для подобного нет специальных настроек (есть макрос `OPTIMIZER_DEBUG`, но он будет выводить уже готовые отношения, а нам нужно проследить порядок выбора, поэтому не подходит).

Для DPsize порядок будет таким:

```text
{1, 2, 3} {5}
{1, 3, 5} {2}
{2, 3, 5} {1}
{1, 5} {2, 3}
```

Для DPhyp — таким:

```text
{1} {2, 3, 5}
{1, 5} {2, 3}
{1, 3, 5} {2}
{1, 2, 3} {5}
```

> Числа - это ID отношений:
>
> 1 - `cast_info ci`
> 2 - `keyword k`
> 3 - `movie_keyword mk`
> 5 - `title t`

Несмотря на разницу в порядке следования, обрабатываются одни и те же пары, то есть мы ничего не теряем. Но почему же тогда разная стоимость? Давайте зайдем с другой стороны — что это за числа `2108` и `5339` в планах запроса? Если посмотреть в коде, то это поле `rows` у структуры `Path`. А как это поле инициализируется? В коде же мы увидим, что `rows` у структуры `Path` инициализируется полем `rows` у `RelOptInfo`, причем это делается во всех типах узлов плана (небольшая часть примеров):

```c++
/* https://github.com/postgres/postgres/blob/144ad723a4484927266a316d1c9550d56745ff67/src/backend/optimizer/path/costsize.c#L3375 */
void
final_cost_nestloop(PlannerInfo *root, NestPath *path, JoinCostWorkspace *workspace, JoinPathExtraData *extra)
{
    /* ... */
    if (path->jpath.path.param_info)
        path->jpath.path.rows = path->jpath.path.param_info->ppi_rows;
    else
        path->jpath.path.rows = path->jpath.path.parent->rows;
    /* ... */
}

/* https://github.com/postgres/postgres/blob/144ad723a4484927266a316d1c9550d56745ff67/src/backend/optimizer/path/costsize.c#L3873 */
void
final_cost_mergejoin(PlannerInfo *root, MergePath *path, JoinCostWorkspace *workspace, JoinPathExtraData *extra)
{
    /* ... */
    if (path->jpath.path.param_info)
        path->jpath.path.rows = path->jpath.path.param_info->ppi_rows;
    else
        path->jpath.path.rows = path->jpath.path.parent->rows;
    /* ... */
}

/* https://github.com/postgres/postgres/blob/144ad723a4484927266a316d1c9550d56745ff67/src/backend/optimizer/path/costsize.c#L4305 */
void
final_cost_hashjoin(PlannerInfo *root, HashPath *path, JoinCostWorkspace *workspace, JoinPathExtraData *extra)
{
    /* ... */
    if (path->jpath.path.param_info)
        path->jpath.path.rows = path->jpath.path.param_info->ppi_rows;
    else
        path->jpath.path.rows = path->jpath.path.parent->rows;
    /* ... */
}
```

Хорошо, а откуда тогда `rows` берется в `RelOptInfo`? Если поискать по коду, то для JOIN мы найдем *единственное* место ее инициализации — `set_joinrel_size_estimates`. Вызывается он в двух местах: `build_join_rel` — создает *новый* `RelOptInfo` типа JOIN и `build_child_join_rel` — то же самое, но для наследуемых таблиц (например, сюда попадают партиции). В нашем случае никаких партиций нет, поэтому используется `build_join_rel`. Финальная черта — где же в нем выставляется оценка количества строк? Ответ — при *первом создании* структуры вызывается `set_joinrel_size_estimates`, которая выставляет это поле, *оценивая по текущей паре* соединяемых отношений. Другими словами, оценка возвращаемого количества строк происходит единожды, а дальше мы используем эту оценку во всех случаях. Звучит вполне логично, так как на каждое множество отношений предикаты тоже фиксированы, поэтому количество возвращаемых строк не должно зависеть от физической реализации оператора. Но почему же тогда оценка так сильно разнится? Для этого еще раз проведем трейсинг, но на этот раз подсчитаем все вызовы и все оценки, которые мы делаем. Построим дерево, узлами которого будут множества отношений, а дочерними узлами — те, из которых создается родитель. Так как в запросе используется только `JOIN INNER`, формула вычисления количества кортежей будет такова: `nrows = outer_rows * inner_rows * jselect`:

- `nrows` — итоговое количество кортежей;
- `outer_rows` — количество кортежей во внешней части;
- `inner_rows` — количество кортежей во внутренней части;
- `jselec` — селективность предикатов.

Для DPsize стек вызовов будет следующим (под множествами написана селективность предикатов, ребра содержат количество кортежей на выходе):

```text
                     {1, 2, 3, 5}
                        3.95e-7
                    /           \
                9813           1375372
                 /                   \
            {1, 2, 3}                {5}
             7.45e-6  
            /      \ 
     164574168      8
        /            \
    {1, 3}           {2}
     1e-6
    /     \
36245584  4523930 
  /          \
{1}          {3}  
```

Указанная селективность сильно обрезана, чтобы не занимать много места, так как числа длинные, но даже без этого можно подсчитать, что `9813 * 1375372 * 3.95e-7 = 5332`. Если добавить отброшенные разряды, наберется на ожидаемое число — `5339`.

Теперь посмотрим, что случилось в DPhyp:

```text
                 {1, 2, 3, 5}
                    3.95e-7
                  /         \   
             36245584        147
               /               \
            {1}             {2, 3, 5}
                              7.45e-6
                            /        \
                           8       2461152
                          /             \
                        {2}           {3, 5}
                                      3.95e-7
                                     /      \
                                 4523930  1375372
                                   /          \
                                 {3}          {5} 
```

Что и следовало ожидать: `36245584 * 147 * 3.95e-7 = 2104`, с округлением это дает `2108`.

Итак, мы нашли исходную проблему — плохой выбор первоначальной оценки. Действительно ли она плоха? В данном случае да, так как если выполнить запрос, то получим следующие результаты:

```text
                                                                             QUERY PLAN                                             
--------------------------------------------------------------------------------------------------------------------------------------------------------------------
 Aggregate  (cost=13720.08..13720.09 rows=1 width=96) (actual time=8753.807..8753.810 rows=1 loops=1)
   ->  Nested Loop  (cost=8.10..13704.27 rows=2108 width=48) (actual time=0.637..8530.663 rows=785477 loops=1)
         ->  Nested Loop  (cost=7.67..12744.50 rows=2108 width=37) (actual time=0.623..2643.045 rows=785477 loops=1)
               Join Filter: (ci.movie_id = t.id)
               ->  Nested Loop  (cost=7.23..12483.46 rows=147 width=41) (actual time=0.610..405.496 rows=14165 loops=1)
                     ->  Nested Loop  (cost=6.80..12351.21 rows=270 width=20) (actual time=0.597..140.721 rows=35548 loops=1)
                           ->  Seq Scan on keyword k  (cost=0.00..3691.40 rows=8 width=20) (actual time=0.143..37.305 rows=8 loops=1)
                                 Filter: (keyword = ANY ('{superhero,sequel,second-part,marvel-comics,based-on-comic,tv-special,fight,violence}'::text[]))
                                 Rows Removed by Filter: 134162
                           ->  Bitmap Heap Scan on movie_keyword mk  (cost=6.80..1079.43 rows=305 width=8) (actual time=0.993..12.278 rows=4444 loops=8)
                                 Recheck Cond: (k.id = keyword_id)
                                 Heap Blocks: exact=23488
                                 ->  Bitmap Index Scan on keyword_id_movie_keyword  (cost=0.00..6.72 rows=305 width=0) (actual time=0.501..0.501 rows=4444 loops=8)
                                       Index Cond: (keyword_id = k.id)
                     ->  Index Scan using title_pkey on title t  (cost=0.43..0.49 rows=1 width=21) (actual time=0.007..0.007 rows=0 loops=35548)
                           Index Cond: (id = mk.movie_id)
                           Filter: (production_year > 2000)
                           Rows Removed by Filter: 1
               ->  Index Scan using movie_id_cast_info on cast_info ci  (cost=0.44..1.33 rows=36 width=8) (actual time=0.008..0.148 rows=55 loops=14165)
                     Index Cond: (movie_id = mk.movie_id)
         ->  Index Scan using name_pkey on name n  (cost=0.43..0.46 rows=1 width=19) (actual time=0.007..0.007 rows=1 loops=785477)
               Index Cond: (id = ci.person_id)
 Planning Time: 1.419 ms
 Execution Time: 8753.873 ms
(24 rows)
```

В реальности узел дал 785477 кортежей, и ошибка составляет (кратность):

- DPhyp: 370;
- DPsize: 150.

Ошиблись в оценке количества кортежей более чем в два раза, причем в худшую сторону — недосчитались. Но и это еще не все. Вспомните первый пример — запрос с единственным гиперребром. Он планируется очень быстро, но вот если сделать шаг в сторону, например, перенести `t7.x` в правую строну выражения, мы получим такой план:

```text
-----------------------------------------------------------------------------------------------------------------------------------------------
 Nested Loop  (cost=0.00..215344.72 rows=1594323 width=56)
   Join Filter: ((((((t1.x + t2.x) + t3.x) + t4.x) + t5.x) + t6.x) > (((((((t7.x + t8.x) + t9.x) + t10.x) + t11.x) + t12.x) + t13.x) + t14.x))
   ->  Nested Loop  (cost=0.00..93.00 rows=6561 width=32)
         ->  Nested Loop  (cost=0.00..5.39 rows=81 width=16)
               ->  Nested Loop  (cost=0.00..2.18 rows=9 width=8)
                     ->  Seq Scan on t7  (cost=0.00..1.03 rows=3 width=4)
                     ->  Materialize  (cost=0.00..1.04 rows=3 width=4)
                           ->  Seq Scan on t8  (cost=0.00..1.03 rows=3 width=4)
               ->  Materialize  (cost=0.00..2.23 rows=9 width=8)
                     ->  Nested Loop  (cost=0.00..2.18 rows=9 width=8)
                           ->  Seq Scan on t9  (cost=0.00..1.03 rows=3 width=4)
                           ->  Materialize  (cost=0.00..1.04 rows=3 width=4)
                                 ->  Seq Scan on t10  (cost=0.00..1.03 rows=3 width=4)
         ->  Materialize  (cost=0.00..5.80 rows=81 width=16)
               ->  Nested Loop  (cost=0.00..5.39 rows=81 width=16)
                     ->  Nested Loop  (cost=0.00..2.18 rows=9 width=8)
                           ->  Seq Scan on t11  (cost=0.00..1.03 rows=3 width=4)
                           ->  Materialize  (cost=0.00..1.04 rows=3 width=4)
                                 ->  Seq Scan on t12  (cost=0.00..1.03 rows=3 width=4)
                     ->  Materialize  (cost=0.00..2.23 rows=9 width=8)
                           ->  Nested Loop  (cost=0.00..2.18 rows=9 width=8)
                                 ->  Seq Scan on t13  (cost=0.00..1.03 rows=3 width=4)
                                 ->  Materialize  (cost=0.00..1.04 rows=3 width=4)
                                       ->  Seq Scan on t14  (cost=0.00..1.03 rows=3 width=4)
   ->  Materialize  (cost=0.00..19.94 rows=729 width=24)
         ->  Nested Loop  (cost=0.00..16.29 rows=729 width=24)
               ->  Nested Loop  (cost=0.00..3.56 rows=27 width=12)
                     ->  Nested Loop  (cost=0.00..2.18 rows=9 width=8)
                           ->  Seq Scan on t2  (cost=0.00..1.03 rows=3 width=4)
                           ->  Materialize  (cost=0.00..1.04 rows=3 width=4)
                                 ->  Seq Scan on t3  (cost=0.00..1.03 rows=3 width=4)
                     ->  Materialize  (cost=0.00..1.04 rows=3 width=4)
                           ->  Seq Scan on t1  (cost=0.00..1.03 rows=3 width=4)
               ->  Materialize  (cost=0.00..3.69 rows=27 width=12)
                     ->  Nested Loop  (cost=0.00..3.56 rows=27 width=12)
                           ->  Nested Loop  (cost=0.00..2.18 rows=9 width=8)
                                 ->  Seq Scan on t5  (cost=0.00..1.03 rows=3 width=4)
                                 ->  Materialize  (cost=0.00..1.04 rows=3 width=4)
                                       ->  Seq Scan on t6  (cost=0.00..1.03 rows=3 width=4)
                           ->  Materialize  (cost=0.00..1.04 rows=3 width=4)
                                 ->  Seq Scan on t4  (cost=0.00..1.03 rows=3 width=4)
 Planning Time: 9.477 ms
(42 rows)
```

Да, немного медленнее — 9 мс вместо 4 мс, но ведь это все равно быстро. Да, быстро, только вот дело уже не в скорости. Посмотрите, что дает DPsize:

```text
                                                                  QUERY PLAN                                   
-----------------------------------------------------------------------------------------------------------------------------------------------
 Nested Loop  (cost=0.00..215311.78 rows=1594323 width=56)
   Join Filter: ((((((t1.x + t2.x) + t3.x) + t4.x) + t5.x) + t6.x) > (((((((t7.x + t8.x) + t9.x) + t10.x) + t11.x) + t12.x) + t13.x) + t14.x))
   ->  Nested Loop  (cost=0.00..36.36 rows=2187 width=28)
         ->  Nested Loop  (cost=0.00..5.39 rows=81 width=16)
               ->  Nested Loop  (cost=0.00..2.18 rows=9 width=8)
                     ->  Seq Scan on t4  (cost=0.00..1.03 rows=3 width=4)
                     ->  Materialize  (cost=0.00..1.04 rows=3 width=4)
                           ->  Seq Scan on t5  (cost=0.00..1.03 rows=3 width=4)
               ->  Materialize  (cost=0.00..2.23 rows=9 width=8)
                     ->  Nested Loop  (cost=0.00..2.18 rows=9 width=8)
                           ->  Seq Scan on t6  (cost=0.00..1.03 rows=3 width=4)
                           ->  Materialize  (cost=0.00..1.04 rows=3 width=4)
                                 ->  Seq Scan on t7  (cost=0.00..1.03 rows=3 width=4)
         ->  Materialize  (cost=0.00..3.69 rows=27 width=12)
               ->  Nested Loop  (cost=0.00..3.56 rows=27 width=12)
                     ->  Nested Loop  (cost=0.00..2.18 rows=9 width=8)
                           ->  Seq Scan on t1  (cost=0.00..1.03 rows=3 width=4)
                           ->  Materialize  (cost=0.00..1.04 rows=3 width=4)
                                 ->  Seq Scan on t2  (cost=0.00..1.03 rows=3 width=4)
                     ->  Materialize  (cost=0.00..1.04 rows=3 width=4)
                           ->  Seq Scan on t3  (cost=0.00..1.03 rows=3 width=4)
   ->  Materialize  (cost=0.00..47.29 rows=2187 width=28)
         ->  Nested Loop  (cost=0.00..36.36 rows=2187 width=28)
               ->  Nested Loop  (cost=0.00..5.39 rows=81 width=16)
                     ->  Nested Loop  (cost=0.00..2.18 rows=9 width=8)
                           ->  Seq Scan on t11  (cost=0.00..1.03 rows=3 width=4)
                           ->  Materialize  (cost=0.00..1.04 rows=3 width=4)
                                 ->  Seq Scan on t12  (cost=0.00..1.03 rows=3 width=4)
                     ->  Materialize  (cost=0.00..2.23 rows=9 width=8)
                           ->  Nested Loop  (cost=0.00..2.18 rows=9 width=8)
                                 ->  Seq Scan on t13  (cost=0.00..1.03 rows=3 width=4)
                                 ->  Materialize  (cost=0.00..1.04 rows=3 width=4)
                                       ->  Seq Scan on t14  (cost=0.00..1.03 rows=3 width=4)
               ->  Materialize  (cost=0.00..3.69 rows=27 width=12)
                     ->  Nested Loop  (cost=0.00..3.56 rows=27 width=12)
                           ->  Nested Loop  (cost=0.00..2.18 rows=9 width=8)
                                 ->  Seq Scan on t8  (cost=0.00..1.03 rows=3 width=4)
                                 ->  Materialize  (cost=0.00..1.04 rows=3 width=4)
                                       ->  Seq Scan on t9  (cost=0.00..1.03 rows=3 width=4)
                           ->  Materialize  (cost=0.00..1.04 rows=3 width=4)
                                 ->  Seq Scan on t10  (cost=0.00..1.03 rows=3 width=4)
 Planning Time: 3337.097 ms
(42 rows)
```

Время планирования действительно гораздо дольше, но внимательнее всмотритесь в план запроса. Первое, что надо заметить, — стоимость меньше. Взгляните, за счет чего:

```text
                     ->  Nested Loop  (cost=0.00..2.18 rows=9 width=8)
                           ->  Seq Scan on t6  (cost=0.00..1.03 rows=3 width=4)
                           ->  Materialize  (cost=0.00..1.04 rows=3 width=4)
                                 ->  Seq Scan on t7  (cost=0.00..1.03 rows=3 width=4)
```

Да, DPsize смог *найти неявную связь* между отношениями, даже если они были по разную сторону операндов. DPhyp такого не сделает, так как это разные стороны ребра, а по его правилам такое делать запрещено — нельзя соединять отдельные узлы разных гиперузлов, если они по разные стороны гиперребра, только все вместе. Из этого можно заключить, что DPhyp — это очень хорошая, но все же *эвристика*. Но, к сожалению, и это не все проблемы.

Для создания гиперребер используется три различных источника, но они не покрывают всех вариантов. Проблема кроется в `joinclauses`. Во время работы PostgreSQL создает все возможные варианты выражения с разным набором необходимых отношений левой и правой части. Это позволяет рассмотреть разные варианты расположения выражения в дереве. Проблема в том, что используемые там индексы отношений могут относиться не к таблицам, а к индексам узлов JOIN (`RangeTblEntry` типа `RTE_JOIN`). И они там не просто так, они "подсказывают" планировщику, какие отношения можно использовать и как переопределить отношения. Для понимания посмотрим на такой запрос (взят из регресс-тестов самого PostgreSQL):

```sql
select t1.* from
  text_tbl t1
  left join (select *, '***'::text as d1 from int8_tbl i8b1) b1
    left join int8_tbl i8
      left join (select *, null::int as d2 from int8_tbl i8b2) b2
      on (i8.q1 = b2.q1)
    on (b2.d2 = b1.q2)
  on (t1.f1 = b1.d1)
  left join int4_tbl i4
  on (i8.q2 = i4.f1);
```

Проблемным здесь является предикат `i8.q2 = i4.f1` — для него хранится *три* копии с разным набором отношений левой и правой части:

- `{3}       - {8}`
- `{3, 6}    - {8}`
- `{3, 6, 7} - {8}`

3 и 8 — индексы, соответствующие таблицам `i8` и `i4` соответственно. Но что это за 6 и 7? Это индексы JOIN'ов:

```sql
-- 6
(select *, '***'::text as d1 from int8_tbl i8b1) b1
    left join int8_tbl i8
      left join (select *, null::int as d2 from int8_tbl i8b2) b2
      on (i8.q1 = b2.q1)
    on (b2.d2 = b1.q2)

-- 7
text_tbl t1
  left join (select *, '***'::text as d1 from int8_tbl i8b1) b1
    left join int8_tbl i8
      left join (select *, null::int as d2 from int8_tbl i8b2) b2
      on (i8.q1 = b2.q1)
    on (b2.d2 = b1.q2)
  on (t1.f1 = b1.d1)
```

Это отражает ограничения: `i8.q2 = i4.f1` применяется не к самому `i8`, а к результату `LEFT JOIN`а.

---

Хорошо, но почему тогда время выполнения больше? Если мы не рассматриваем дополнительные варианты, то это время должно быть меньше. Дело вот в чем.

Если посмотреть на код ванильного планировщика, то можем увидеть, что он достаточно умный. [Функция `join_search_one_level`](https://github.com/postgres/postgres/blob/0810fbb02dbe70b8a7a7bcc51580827b8bbddbdc/src/backend/optimizer/path/joinrels.c#L73) отвечает за обработку 1 уровня DPsize:

<spoiler title="join_search_one_level">

```c++
/* src/backend/optimizer/path/joinrels.c */
void
join_search_one_level(PlannerInfo *root, int level)
{
	List	  **joinrels = root->join_rel_level;
	ListCell   *r;
	int			k;

	root->join_cur_level = level;

	/*
	 * Соединяем все предыдущие уровни, с единственной таблицей.
	 */
	foreach(r, joinrels[level - 1])
	{
		RelOptInfo *old_rel = (RelOptInfo *) lfirst(r);

		if (old_rel->joininfo != NIL || old_rel->has_eclass_joins ||
			has_join_restriction(root, old_rel))
		{
			int			first_rel;

			if (level == 2)
				first_rel = foreach_current_index(r) + 1;
			else
				first_rel = 0;

			make_rels_by_clause_joins(root, old_rel, joinrels[1], first_rel);
		}
		else
		{
			make_rels_by_clauseless_joins(root,
										  old_rel,
										  joinrels[1]);
		}
	}

	/*
	 * Создание "ветвистых" планов - логика DPsize, где рассматриваются
     * пары из отношений с несколькими таблицами, по обеим сторонам
	 */
	for (k = 2;; k++)
	{
		int			other_level = level - k;

		foreach(r, joinrels[k])
		{
			RelOptInfo *old_rel = (RelOptInfo *) lfirst(r);
			int			first_rel;
			ListCell   *r2;

			if (k == other_level)
				first_rel = foreach_current_index(r) + 1;
			else
				first_rel = 0;

			for_each_from(r2, joinrels[other_level], first_rel)
			{
				RelOptInfo *new_rel = (RelOptInfo *) lfirst(r2);

                /* Создаем план, только есть подходящий предикат */
				if (!bms_overlap(old_rel->relids, new_rel->relids))
				{
					if (have_relevant_joinclause(root, old_rel, new_rel) ||
						have_join_order_restriction(root, old_rel, new_rel))
					{
						(void) make_join_rel(root, old_rel, new_rel);
					}
				}
			}
		}
	}

	/*
     * Создаем CROSS JOIN, если на текущем уровне ничего не смогли создать
	 */
	if (joinrels[level] == NIL)
	{
		foreach(r, joinrels[level - 1])
		{
			RelOptInfo *old_rel = (RelOptInfo *) lfirst(r);

			make_rels_by_clauseless_joins(root,
										  old_rel,
										  joinrels[1]);
		}
	}
}
```

</spoiler>

Ее логика такая:

1. Вначале создаем left-deep/right-deep планы — создаем текущий уровень присоединением 1 таблицы к предыдущему уровню (даже если нет предиката, то есть создаем `CROSS JOIN`).
2. Затем запускаем основную логику DPsize с рассмотрением всех возможных пар, дающих по итогу нужное количество.
3. В конце, если ничего не смогли найти, то создаем узлы `CROSS JOIN`'а.

Основная оптимизация заключается во 2 шаге — мы *не* создаем лишние `CROSS JOIN`, если они не нужны. Это, по факту, имитация поведения DPhyp, так как его идея в этом и заключается — соединять отношения, только если между ними есть связь.

Остальные микросекунды мы тратим на операционную деятельность:

- Созданием гиперграфа.
- Обход соседей.
- Работа с еще одной хэш-таблицей.

Все эти лишние такты накапливаются и выливаются в еще более длительное выполнение.

## Итоги

Расширение, конечно, пока еще довольно сырое. Существующая инфраструктура заточена под особенности PostgreSQL, поэтому для реализации одного функционала приходится искать обходные пути, а другой работает "со скрипом".

Предвосхищаю вопрос: а зачем все это понадобилось? R&D. Как я сказал в начале, планировщик — важная деталь работы СУБД, поэтому исследования в этой области могут в какой-то момент обернуться существенным выигрышем (ключевое слово — "могут"). К сожалению, окупаемость конкретно этой инвестиции сейчас отрицательная — на практике этот алгоритм создает планы и не лучше, и не быстрее.

Подытожим недостатки:

1. Теряются некоторые возможные оптимизации: гиперребра создаются из предикатов, которые на данный момент нельзя полностью трансформировать в гиперребра из-за индексов RangeTable, указывающих НЕ на отношения (например, на JOIN'ы).
2. Несвязные подграфы требуют отдельной обработки: пользователь сам должен указывать расширению как действовать (параметр `cj_strategy`).
3. Оценка страдает из-за неоптимальных первых аргументов: порядок обрабатываемых пар отношений для JOIN'ов важен, но сейчас он не соотносится с тем, что делает встроенный планировщик.
4. Довольно долгое выполнение: сейчас используется встроенный `make_join_rel`, но он занимает львиную долю времени.

При всем этом есть и хорошая новость: все поправимо. Ограничения продиктованы одной лишь реализацией, то есть это не какие-то фундаментальные ограничения. Никто не запрещает нам написать свой `make_join_rel`, оптимизированный под DPhyp. В крайнем случае мы можем пропатчить ядро и добавить то, чего нам недостает. Тем более мы нашли как минимум один запрос, который смогли ускорить в сотни раз, а это может открыть целую область применимости, и значит, работа проделана не зря.

Всем слона.

Ссылки:

- [Расширение pg_dphyp](https://github.com/TantorLabs/pg_dphyp)
- [Dynamic Programming Strikes Back](https://15721.courses.cs.cmu.edu/spring2018/papers/16-optimizer2/p539-moerkotte.pdf) — DPhyp
- [Adaptive Optimization of Very Large Join Queries](https://db.in.tum.de/~radke/papers/hugejoins.pdf) — планирование огромных запросов
