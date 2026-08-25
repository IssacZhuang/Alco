#
 
S
l
a
n
g
 
c
o
d
i
n
g
 
s
t
a
n
d
a
r
d
A
l
c
o
 
c
o
m
p
i
l
e
s
 
s
h
a
d
e
r
s
 
w
i
t
h
 
t
h
e
 
m
o
d
e
r
n
 
S
l
a
n
g
 
A
P
I
 
a
n
d
 
t
h
e
 
p
i
n
n
e
d
 
S
l
a
n
g
 
2
0
2
6
.
1
6
n
a
t
i
v
e
 
r
u
n
t
i
m
e
.
 
T
h
e
s
e
 
r
u
l
e
s
 
a
p
p
l
y
 
t
o
 
e
n
g
i
n
e
,
 
W
o
r
l
d
3
D
,
 
s
a
n
d
b
o
x
 
a
n
d
 
t
e
s
t
 
s
h
a
d
e
r
m
o
d
u
l
e
s
.
#
#
 
M
o
d
u
l
e
 
h
e
a
d
e
r
 
a
n
d
 
n
a
m
i
n
g
E
v
e
r
y
 
`
.
s
l
a
n
g
`
 
f
i
l
e
 
c
o
n
t
a
i
n
s
 
e
x
a
c
t
l
y
 
o
n
e
 
l
a
n
g
u
a
g
e
 
d
i
r
e
c
t
i
v
e
 
f
o
l
l
o
w
e
d
 
b
y
 
e
x
a
c
t
l
y
o
n
e
 
m
o
d
u
l
e
 
d
e
c
l
a
r
a
t
i
o
n
:
`
`
`
s
l
a
n
g
#
l
a
n
g
u
a
g
e
 
s
l
a
n
g
 
2
0
2
5
m
o
d
u
l
e
 
a
l
c
o
_
r
e
n
d
e
r
i
n
g
_
e
x
a
m
p
l
e
;
`
`
`
C
o
m
m
e
n
t
s
 
m
a
y
 
p
r
e
c
e
d
e
 
t
h
e
 
d
i
r
e
c
t
i
v
e
.
 
F
i
l
e
 
n
a
m
e
s
 
a
r
e
 
l
o
w
e
r
c
a
s
e
 
k
e
b
a
b
-
c
a
s
e
(
`
a
l
c
o
-
r
e
n
d
e
r
i
n
g
-
e
x
a
m
p
l
e
.
s
l
a
n
g
`
,
 
`
g
a
u
s
s
i
a
n
-
b
l
u
r
-
r
g
b
a
1
6
f
.
s
l
a
n
g
`
,
 
`
f
x
a
a
.
s
l
a
n
g
`
)
;
m
o
d
u
l
e
 
n
a
m
e
s
 
a
r
e
 
t
h
e
 
f
i
l
e
 
s
t
e
m
 
i
n
 
l
o
w
e
r
c
a
s
e
 
s
n
a
k
e
_
c
a
s
e
 
a
n
d
 
p
a
i
r
 
w
i
t
h
 
i
t
 
e
x
a
c
t
l
y
(
`
m
o
d
u
l
e
 
g
a
u
s
s
i
a
n
_
b
l
u
r
_
r
g
b
a
1
6
f
;
`
,
 
`
m
o
d
u
l
e
 
f
x
a
a
;
`
)
.
 
A
c
r
o
n
y
m
s
 
s
t
a
y
 
i
n
t
a
c
t
 
—
`
f
x
a
a
`
,
 
n
e
v
e
r
 
`
f
_
x
_
a
_
a
`
.
 
T
h
e
 
p
a
i
r
i
n
g
 
k
e
e
p
s
 
s
l
a
n
g
'
s
 
i
m
p
o
r
t
 
p
r
o
b
i
n
g
(
u
n
d
e
r
s
c
o
r
e
 
↔
 
d
a
s
h
)
 
r
e
s
o
l
v
a
b
l
e
 
o
n
 
c
a
s
e
-
s
e
n
s
i
t
i
v
e
 
a
s
s
e
t
 
s
y
s
t
e
m
s
 
(
L
i
n
u
x
/
A
n
d
r
o
i
d
)
a
n
d
 
i
s
 
e
n
f
o
r
c
e
d
 
b
y
 
`
S
l
a
n
g
S
o
u
r
c
e
C
o
n
v
e
n
t
i
o
n
T
e
s
t
 
r
e
j
e
c
t
s
 
t
e
x
t
u
a
l
i
n
c
l
u
d
e
s
,
 
a
n
y
 
`
r
e
g
i
s
t
e
r
(
.
.
.
)
`
 
d
e
c
l
a
r
a
t
i
o
n
,
 
`
[
[
v
k
:
:
b
i
n
d
i
n
g
]
]
`
 
d
e
c
o
r
a
t
i
o
n
s
,
s
a
m
p
l
i
n
g
 
m
a
c
r
o
s
,
 
a
n
d
 
c
b
u
f
f
e
r
 
b
l
o
c
k
s
 
o
u
t
s
i
d
e
 
p
u
s
h
 
c
o
n
s
t
a
n
t
s
,
 
p
a
i
r
i
n
g
 
e
v
e
r
y
f
i
l
e
 
w
i
t
h
 
i
t
s
 
m
o
d
u
l
e
 
n
a
m
e
.
.
.
)
`
 
d
e
c
l
a
r
a
t
i
o
n
 
w
i
t
h
o
u
t
 
a
 
s
e
t
,
`
[
[
v
k
:
:
b
i
n
d
i
n
g
]
]
`
 
d
e
c
o
r
a
t
i
o
n
s
,
 
a
n
d
 
r
e
g
i
s
t
e
r
s
 
o
u
t
s
i
d
e
 
c
b
u
f
f
e
r
/
C
o
n
s
t
a
n
t
B
u
f
f
e
r
b
l
o
c
k
s
.
 
`
S
l
a
n
g
B
l
o
c
k
B
i
n
d
i
n
g
T
e
s
t
`
 
p
i
n
s
 
t
h
e
 
P
a
r
a
m
e
t
e
r
B
l
o
c
k
 
r
e
f
l
e
c
t
i
o
n
 
c
o
n
t
r
a
c
t
 
(
o
n
e
 
b
l
o
c
k
 
p
e
r
 
s
e
t
 
i
n
 
d
e
c
l
a
r
a
t
i
o
n
 
o
r
d
e
r
,
 
a
u
t
o
 
U
B
O
 
a
t
 
b
i
n
d
i
n
g
 
0
,
 
b
a
r
e
 
m
e
m
b
e
r
 
n
a
m
e
s
,
 
s
e
t
-
n
u
m
b
e
r
-
f
r
e
e
 
d
i
s
c
o
v
e
r
y
)
.
 
R
e
n
d
e
r
i
n
g
 
a
n
d
 
W
o
r
l
d
3
D
v
a
l
i
d
a
t
i
o
n
 
c
o
m
p
i
l
e
 
e
v
e
r
y
 
e
n
t
r
y
-
p
o
i
n
t
 
m
o
d
u
l
e
 
a
n
d
 
i
n
s
p
e
c
t
 
S
l
a
n
g
 
r
e
f
l
e
c
t
i
o
n
h
e
a
d
l
e
s
s
l
y
-
 
A
 
s
u
r
f
a
c
e
 
d
e
c
l
a
r
e
s
 
i
t
s
 
r
e
s
o
u
r
c
e
s
 
i
n
 
i
t
s
 
o
w
n
 
`
P
a
r
a
m
e
t
e
r
B
l
o
c
k
`
 
(
a
n
y
 
b
l
o
c
k
 
 
n
a
m
e
;
 
t
h
e
 
m
a
t
e
r
i
a
l
 
s
e
t
 
i
s
 
w
h
e
r
e
v
e
r
 
t
h
e
 
c
o
m
p
o
s
e
d
 
l
a
y
o
u
t
 
p
u
t
s
 
i
t
 
—
 
t
h
e
 
e
n
g
i
n
e
 
 
r
e
a
d
s
 
t
e
x
t
u
r
e
 
s
l
o
t
s
 
f
r
o
m
 
t
h
e
 
s
u
r
f
a
c
e
 
m
o
d
u
l
e
'
s
 
r
e
f
l
e
c
t
i
o
n
,
 
n
o
t
 
a
 
s
e
t
 
n
u
m
b
e
r
)
.
 
 
T
h
e
 
e
n
g
i
n
e
 
b
i
n
d
s
 
m
e
m
b
e
r
s
 
b
y
 
b
a
r
e
 
n
a
m
e
.
-
 
O
n
e
 
b
l
o
c
k
 
=
 
o
n
e
 
w
h
o
l
e
 
d
e
s
c
r
i
p
t
o
r
 
s
e
t
;
 
b
l
o
c
k
s
 
t
a
k
e
 
s
e
t
s
 
i
n
 
d
e
c
l
a
r
a
t
i
o
n
 
o
r
d
e
r
 
 
(
e
n
t
r
y
 
m
o
d
u
l
e
 
b
l
o
c
k
s
 
f
i
r
s
t
,
 
t
h
e
n
 
c
o
m
p
a
n
i
o
n
 
m
o
d
u
l
e
s
'
,
 
t
h
e
n
 
i
m
p
o
r
t
e
d
 
m
o
d
u
l
e
s
'
)
.
 
 
B
a
r
e
 
g
l
o
b
a
l
s
 
(
n
o
 
b
l
o
c
k
)
 
f
i
l
l
 
s
e
t
 
0
 
b
e
f
o
r
e
 
a
n
y
 
b
l
o
c
k
 
—
 
n
e
w
 
c
o
d
e
 
s
h
o
u
l
d
 
n
o
t
 
m
i
x
 
 
t
h
e
m
;
 
w
r
a
p
 
g
l
o
b
a
l
s
 
i
n
 
a
 
b
l
o
c
k
.
-
 
A
 
b
l
o
c
k
 
w
i
t
h
 
o
r
d
i
n
a
r
y
 
d
a
t
a
 
g
e
t
s
 
a
n
 
a
u
t
o
m
a
t
i
c
a
l
l
y
-
i
n
t
r
o
d
u
c
e
d
 
u
n
i
f
o
r
m
 
b
u
f
f
e
r
 
a
t
 
 
b
i
n
d
i
n
g
 
0
 
u
n
d
e
r
 
t
h
e
 
b
l
o
c
k
 
v
a
r
i
a
b
l
e
'
s
 
n
a
m
e
;
 
r
e
s
o
u
r
c
e
 
m
e
m
b
e
r
s
 
c
o
n
t
i
n
u
e
 
a
f
t
e
r
 
i
t
 
 
i
n
 
d
e
c
l
a
r
a
t
i
o
n
 
o
r
d
e
r
.
 
A
 
r
e
s
o
u
r
c
e
-
o
n
l
y
 
b
l
o
c
k
 
e
m
i
t
s
 
n
o
 
U
B
O
 
a
n
d
 
i
t
s
 
m
e
m
b
e
r
s
 
s
t
a
r
t
 
 
a
t
 
b
i
n
d
i
n
g
 
0
.
-
 
B
l
o
c
k
s
 
a
n
d
 
t
h
e
i
r
 
s
t
r
u
c
t
s
 
n
e
e
d
 
`
p
u
b
l
i
c
`
 
m
e
m
b
e
r
s
 
w
h
e
n
 
s
h
a
r
e
d
 
a
c
r
o
s
s
 
m
o
d
u
l
e
s
 
 
(
S
l
a
n
g
 
2
0
2
5
 
v
i
s
i
b
i
l
i
t
y
 
i
s
 
i
n
t
e
r
n
a
l
 
b
y
 
d
e
f
a
u
l
t
)
.
-
 
B
l
o
c
k
 
m
e
m
b
e
r
s
 
k
e
e
p
 
t
h
e
i
r
 
b
a
r
e
 
f
i
e
l
d
 
n
a
m
e
s
 
i
n
 
r
e
f
l
e
c
t
i
o
n
 
(
`
_
o
u
t
p
u
t
`
,
 
n
o
t
 
 
`
_
p
a
s
s
.
_
o
u
t
p
u
t
`
)
 
—
 
t
h
e
 
s
h
a
d
e
r
 
b
o
d
y
 
q
u
a
l
i
f
i
e
s
 
(
`
_
p
a
s
s
.
_
o
u
t
p
u
t
`
)
,
 
b
u
t
 
t
h
e
 
 
e
n
g
i
n
e
 
b
i
n
d
s
 
b
y
 
t
h
e
 
b
a
r
e
 
n
a
m
e
;
 
b
i
n
d
i
n
g
 
n
u
m
b
e
r
s
 
a
r
e
 
p
r
i
v
a
t
e
 
p
h
y
s
i
c
a
l
 
l
a
y
o
u
t
 
 
a
n
d
 
m
u
s
t
 
n
o
t
 
a
p
p
e
a
r
 
i
n
 
c
a
l
l
e
r
 
l
o
g
i
c
.
W
h
e
n
 
a
 
p
a
s
s
 
n
e
e
d
s
 
a
n
 
u
n
f
i
l
t
e
r
e
d
 
r
a
w
 
d
e
p
t
h
 
v
a
l
u
e
,
 
d
e
c
l
a
r
e
 
`
D
e
p
t
h
T
e
x
t
u
r
e
2
D
`
,
 
c
a
l
l
`
L
o
a
d
`
,
 
a
n
d
 
b
i
n
d
 
t
h
e
 
f
r
a
m
e
b
u
f
f
e
r
'
s
 
n
a
t
i
v
e
 
d
e
p
t
h
 
a
t
t
a
c
h
m
e
n
t
 
w
i
t
h
`
S
e
t
R
e
n
d
e
r
T
e
x
t
u
r
e
D
e
p
t
h
`
.
 
D
o
 
n
o
t
 
a
d
d
 
a
 
c
o
l
o
r
 
d
e
p
t
h
 
m
i
r
r
o
r
 
o
r
 
a
n
 
e
x
p
l
i
c
i
t
l
y
f
o
r
m
a
t
t
e
d
 
`
T
e
x
t
u
r
e
2
D
<
f
l
o
a
t
>
`
 
s
u
b
s
t
i
t
u
t
e
.
D
i
r
e
c
t
 
S
P
I
R
-
V
 
c
o
m
p
i
l
e
r
 
o
u
t
p
u
t
 
i
s
 
m
a
n
d
a
t
o
r
y
.
 
D
o
 
n
o
t
 
s
e
l
e
c
t
 
a
 
c
o
m
p
i
l
e
r
 
b
a
c
k
e
n
d
 
i
n
s
h
a
d
e
r
-
l
o
a
d
i
n
g
 
c
o
d
e
 
o
r
 
i
n
t
r
o
d
u
c
e
 
a
 
v
i
a
-
G
L
S
L
/
g
l
s
l
a
n
g
 
f
a
l
l
b
a
c
k
.
 
O
n
 
V
u
l
k
a
n
,
w
g
p
u
-
n
a
t
i
v
e
'
s
 
S
P
I
R
-
V
 
p
a
s
s
t
h
r
o
u
g
h
 
c
a
p
a
b
i
l
i
t
y
 
s
u
b
m
i
t
s
 
t
h
e
 
v
a
l
i
d
a
t
e
d
 
S
l
a
n
g
 
o
u
t
p
u
t
w
i
t
h
o
u
t
 
a
 
N
a
g
a
 
i
m
p
o
r
t
/
r
e
-
e
m
i
s
s
i
o
n
 
r
o
u
n
d
 
t
r
i
p
.
 
N
o
n
-
V
u
l
k
a
n
 
b
a
c
k
e
n
d
s
 
r
e
t
a
i
n
 
w
g
p
u
'
s
n
o
r
m
a
l
 
t
a
r
g
e
t
 
t
r
a
n
s
l
a
t
i
o
n
 
p
a
t
h
.
#
#
 
V
a
l
i
d
a
t
i
o
n
B
e
f
o
r
e
 
l
a
n
d
i
n
g
 
s
h
a
d
e
r
 
c
h
a
n
g
e
s
,
 
r
u
n
:
`
`
`
t
e
x
t
d
o
t
n
e
t
 
b
u
i
l
d
 
A
l
c
o
.
s
l
n
x
 
-
-
n
o
-
r
e
s
t
o
r
e
d
o
t
n
e
t
 
t
e
s
t
 
T
e
s
t
/
A
l
c
o
.
S
h
a
d
e
r
C
o
m
p
i
l
e
r
.
T
e
s
t
/
A
l
c
o
.
S
h
a
d
e
r
C
o
m
p
i
l
e
r
.
T
e
s
t
.
c
s
p
r
o
j
 
-
-
n
o
-
b
u
i
l
d
d
o
t
n
e
t
 
t
e
s
t
 
T
e
s
t
/
A
l
c
o
.
R
e
n
d
e
r
i
n
g
.
T
e
s
t
/
A
l
c
o
.
R
e
n
d
e
r
i
n
g
.
T
e
s
t
.
c
s
p
r
o
j
 
-
-
n
o
-
b
u
i
l
d
d
o
t
n
e
t
 
t
e
s
t
 
T
e
s
t
/
A
l
c
o
.
W
o
r
l
d
3
D
.
T
e
s
t
/
A
l
c
o
.
W
o
r
l
d
3
D
.
T
e
s
t
.
c
s
p
r
o
j
 
-
-
n
o
-
b
u
i
l
d
`
`
`
`
S
l
a
n
g
S
o
u
r
c
e
C
o
n
v
e
n
t
i
o
n
T
e
s
t
`
 
e
n
f
o
r
c
e
s
 
m
o
d
u
l
e
 
h
e
a
d
e
r
s
,
 
f
i
l
e
/
m
o
d
u
l
e
 
n
a
m
i
n
g
(
k
e
b
a
b
-
c
a
s
e
 
f
i
l
e
s
 
p
a
i
r
e
d
 
w
i
t
h
 
s
n
a
k
e
_
c
a
s
e
 
m
o
d
u
l
e
 
n
a
m
e
s
)
,
 
r
e
m
o
v
e
s
 
l
e
g
a
c
y
 
H
L
S
L
a
n
d
 
r
e
j
e
c
t
s
 
t
e
x
t
u
a
l
 
i
n
c
l
u
d
e
s
,
 
a
n
y
 
`
r
e
g
i
s
t
e
r
(
.
.
.
)
`
 
a
n
n
o
t
a
t
i
o
n
,
`
[
[
v
k
:
:
b
i
n
d
i
n
g
]
]
`
 
d
e
c
o
r
a
t
i
o
n
s
,
 
a
n
d
 
s
a
m
p
l
i
n
g
 
m
a
c
r
o
s
.
 
`
S
l
a
n
g
B
l
o
c
k
B
i
n
d
i
n
g
T
e
s
t
`
 
p
i
n
s
 
t
h
e
 
b
l
o
c
k
 
r
e
f
l
e
c
t
i
o
n
 
c
o
n
t
r
a
c
t
 
(
b
a
r
e
 
m
e
m
b
e
r
n
a
m
e
s
,
 
c
o
m
p
i
l
e
r
-
a
s
s
i
g
n
e
d
 
o
r
d
e
r
,
 
m
u
l
t
i
-
b
l
o
c
k
 
s
e
t
s
)
.
 
R
e
n
d
e
r
i
n
g
 
a
n
d
 
W
o
r
l
d
3
D
v
a
l
i
d
a
t
i
o
n
 
c
o
m
p
i
l
e
 
e
v
e
r
y
 
e
n
t
r
y
-
p
o
i
n
t
 
m
o
d
u
l
e
 
a
n
d
 
i
n
s
p
e
c
t
 
S
l
a
n
g
 
r
e
f
l
e
c
t
i
o
n
h
e
a
d
l
e
s
s
l
y
-
 
P
a
s
s
 
t
e
m
p
l
a
t
e
s
 
d
e
c
l
a
r
e
 
t
h
e
i
r
 
e
n
g
i
n
e
 
r
e
s
o
u
r
c
e
s
 
a
s
 
P
a
r
a
m
e
t
e
r
B
l
o
c
k
s
 
i
n
 
 
d
e
c
l
a
r
a
t
i
o
n
 
o
r
d
e
r
;
 
s
e
t
 
n
u
m
b
e
r
s
 
a
r
e
 
a
n
 
o
u
t
p
u
t
 
o
f
 
t
h
e
 
c
o
m
p
o
s
i
t
i
o
n
,
 
n
e
v
e
r
 
a
n
 
 
i
n
p
u
t
.
-
 
O
n
e
 
b
l
o
c
k
 
=
 
o
n
e
 
w
h
o
l
e
 
d
e
s
c
r
i
p
t
o
r
 
s
e
t
;
 
b
l
o
c
k
s
 
t
a
k
e
 
s
e
t
s
 
i
n
 
d
e
c
l
a
r
a
t
i
o
n
 
o
r
d
e
r
 
 
(
e
n
t
r
y
 
m
o
d
u
l
e
 
b
l
o
c
k
s
 
f
i
r
s
t
,
 
t
h
e
n
 
c
o
m
p
a
n
i
o
n
 
m
o
d
u
l
e
s
'
,
 
t
h
e
n
 
i
m
p
o
r
t
e
d
 
m
o
d
u
l
e
s
'
)
.
 
 
B
a
r
e
 
g
l
o
b
a
l
s
 
(
n
o
 
b
l
o
c
k
)
 
f
i
l
l
 
s
e
t
 
0
 
b
e
f
o
r
e
 
a
n
y
 
b
l
o
c
k
 
—
 
n
e
w
 
c
o
d
e
 
s
h
o
u
l
d
 
n
o
t
 
m
i
x
 
 
t
h
e
m
;
 
w
r
a
p
 
g
l
o
b
a
l
s
 
i
n
 
a
 
b
l
o
c
k
.
-
 
A
 
b
l
o
c
k
 
w
i
t
h
 
o
r
d
i
n
a
r
y
 
d
a
t
a
 
g
e
t
s
 
a
n
 
a
u
t
o
m
a
t
i
c
a
l
l
y
-
i
n
t
r
o
d
u
c
e
d
 
u
n
i
f
o
r
m
 
b
u
f
f
e
r
 
a
t
 
 
b
i
n
d
i
n
g
 
0
 
u
n
d
e
r
 
t
h
e
 
b
l
o
c
k
 
v
a
r
i
a
b
l
e
'
s
 
n
a
m
e
;
 
r
e
s
o
u
r
c
e
 
m
e
m
b
e
r
s
 
c
o
n
t
i
n
u
e
 
a
f
t
e
r
 
i
t
 
 
i
n
 
d
e
c
l
a
r
a
t
i
o
n
 
o
r
d
e
r
.
 
A
 
r
e
s
o
u
r
c
e
-
o
n
l
y
 
b
l
o
c
k
 
e
m
i
t
s
 
n
o
 
U
B
O
 
a
n
d
 
i
t
s
 
m
e
m
b
e
r
s
 
s
t
a
r
t
 
 
a
t
 
b
i
n
d
i
n
g
 
0
.
-
 
B
l
o
c
k
s
 
a
n
d
 
t
h
e
i
r
 
s
t
r
u
c
t
s
 
n
e
e
d
 
`
p
u
b
l
i
c
`
 
m
e
m
b
e
r
s
 
w
h
e
n
 
s
h
a
r
e
d
 
a
c
r
o
s
s
 
m
o
d
u
l
e
s
 
 
(
S
l
a
n
g
 
2
0
2
5
 
v
i
s
i
b
i
l
i
t
y
 
i
s
 
i
n
t
e
r
n
a
l
 
b
y
 
d
e
f
a
u
l
t
)
.
-
 
B
l
o
c
k
 
m
e
m
b
e
r
s
 
k
e
e
p
 
t
h
e
i
r
 
b
a
r
e
 
f
i
e
l
d
 
n
a
m
e
s
 
i
n
 
r
e
f
l
e
c
t
i
o
n
 
(
`
_
o
u
t
p
u
t
`
,
 
n
o
t
 
 
`
_
p
a
s
s
.
_
o
u
t
p
u
t
`
)
 
—
 
t
h
e
 
s
h
a
d
e
r
 
b
o
d
y
 
q
u
a
l
i
f
i
e
s
 
(
`
_
p
a
s
s
.
_
o
u
t
p
u
t
`
)
,
 
b
u
t
 
t
h
e
 
 
e
n
g
i
n
e
 
b
i
n
d
s
 
b
y
 
t
h
e
 
b
a
r
e
 
n
a
m
e
;
 
b
i
n
d
i
n
g
 
n
u
m
b
e
r
s
 
a
r
e
 
p
r
i
v
a
t
e
 
p
h
y
s
i
c
a
l
 
l
a
y
o
u
t
 
 
a
n
d
 
m
u
s
t
 
n
o
t
 
a
p
p
e
a
r
 
i
n
 
c
a
l
l
e
r
 
l
o
g
i
c
.
W
h
e
n
 
a
 
p
a
s
s
 
n
e
e
d
s
 
a
n
 
u
n
f
i
l
t
e
r
e
d
 
r
a
w
 
d
e
p
t
h
 
v
a
l
u
e
,
 
d
e
c
l
a
r
e
 
`
D
e
p
t
h
T
e
x
t
u
r
e
2
D
`
,
 
c
a
l
l
`
L
o
a
d
`
,
 
a
n
d
 
b
i
n
d
 
t
h
e
 
f
r
a
m
e
b
u
f
f
e
r
'
s
 
n
a
t
i
v
e
 
d
e
p
t
h
 
a
t
t
a
c
h
m
e
n
t
 
w
i
t
h
`
S
e
t
R
e
n
d
e
r
T
e
x
t
u
r
e
D
e
p
t
h
`
.
 
D
o
 
n
o
t
 
a
d
d
 
a
 
c
o
l
o
r
 
d
e
p
t
h
 
m
i
r
r
o
r
 
o
r
 
a
n
 
e
x
p
l
i
c
i
t
l
y
f
o
r
m
a
t
t
e
d
 
`
T
e
x
t
u
r
e
2
D
<
f
l
o
a
t
>
`
 
s
u
b
s
t
i
t
u
t
e
.
D
i
r
e
c
t
 
S
P
I
R
-
V
 
c
o
m
p
i
l
e
r
 
o
u
t
p
u
t
 
i
s
 
m
a
n
d
a
t
o
r
y
.
 
D
o
 
n
o
t
 
s
e
l
e
c
t
 
a
 
c
o
m
p
i
l
e
r
 
b
a
c
k
e
n
d
 
i
n
s
h
a
d
e
r
-
l
o
a
d
i
n
g
 
c
o
d
e
 
o
r
 
i
n
t
r
o
d
u
c
e
 
a
 
v
i
a
-
G
L
S
L
/
g
l
s
l
a
n
g
 
f
a
l
l
b
a
c
k
.
 
O
n
 
V
u
l
k
a
n
,
w
g
p
u
-
n
a
t
i
v
e
'
s
 
S
P
I
R
-
V
 
p
a
s
s
t
h
r
o
u
g
h
 
c
a
p
a
b
i
l
i
t
y
 
s
u
b
m
i
t
s
 
t
h
e
 
v
a
l
i
d
a
t
e
d
 
S
l
a
n
g
 
o
u
t
p
u
t
w
i
t
h
o
u
t
 
a
 
N
a
g
a
 
i
m
p
o
r
t
/
r
e
-
e
m
i
s
s
i
o
n
 
r
o
u
n
d
 
t
r
i
p
.
 
N
o
n
-
V
u
l
k
a
n
 
b
a
c
k
e
n
d
s
 
r
e
t
a
i
n
 
w
g
p
u
'
s
n
o
r
m
a
l
 
t
a
r
g
e
t
 
t
r
a
n
s
l
a
t
i
o
n
 
p
a
t
h
.
#
#
 
V
a
l
i
d
a
t
i
o
n
B
e
f
o
r
e
 
l
a
n
d
i
n
g
 
s
h
a
d
e
r
 
c
h
a
n
g
e
s
,
 
r
u
n
:
`
`
`
t
e
x
t
d
o
t
n
e
t
 
b
u
i
l
d
 
A
l
c
o
.
s
l
n
x
 
-
-
n
o
-
r
e
s
t
o
r
e
d
o
t
n
e
t
 
t
e
s
t
 
T
e
s
t
/
A
l
c
o
.
S
h
a
d
e
r
C
o
m
p
i
l
e
r
.
T
e
s
t
/
A
l
c
o
.
S
h
a
d
e
r
C
o
m
p
i
l
e
r
.
T
e
s
t
.
c
s
p
r
o
j
 
-
-
n
o
-
b
u
i
l
d
d
o
t
n
e
t
 
t
e
s
t
 
T
e
s
t
/
A
l
c
o
.
R
e
n
d
e
r
i
n
g
.
T
e
s
t
/
A
l
c
o
.
R
e
n
d
e
r
i
n
g
.
T
e
s
t
.
c
s
p
r
o
j
 
-
-
n
o
-
b
u
i
l
d
d
o
t
n
e
t
 
t
e
s
t
 
T
e
s
t
/
A
l
c
o
.
W
o
r
l
d
3
D
.
T
e
s
t
/
A
l
c
o
.
W
o
r
l
d
3
D
.
T
e
s
t
.
c
s
p
r
o
j
 
-
-
n
o
-
b
u
i
l
d
`
`
`
`
S
l
a
n
g
S
o
u
r
c
e
C
o
n
v
e
n
t
i
o
n
T
e
s
t
`
 
e
n
f
o
r
c
e
s
 
m
o
d
u
l
e
 
h
e
a
d
e
r
s
,
 
f
i
l
e
/
m
o
d
u
l
e
 
n
a
m
i
n
g
(
k
e
b
a
b
-
c
a
s
e
 
f
i
l
e
s
 
p
a
i
r
e
d
 
w
i
t
h
 
s
n
a
k
e
_
c
a
s
e
 
m
o
d
u
l
e
 
n
a
m
e
s
)
,
 
r
e
m
o
v
e
s
 
l
e
g
a
c
y
 
H
L
S
L
a
n
d
 
r
e
j
e
c
t
s
 
t
e
x
t
u
a
l
 
i
n
c
l
u
d
e
s
,
 
a
 
`
r
e
g
i
s
t
e
r
(
.
.
.
)
`
 
d
e
c
l
a
r
a
t
i
o
n
 
w
i
t
h
o
u
t
 
a
 
s
e
t
,
`
[
[
v
k
:
:
b
i
n
d
i
n
g
]
]
`
 
d
e
c
o
r
a
t
i
o
n
s
,
 
a
n
d
 
r
e
g
i
s
t
e
r
s
 
o
u
t
s
i
d
e
 
c
b
u
f
f
e
r
/
C
o
n
s
t
a
n
t
B
u
f
f
e
r
b
l
o
c
k
s
.
 
`
S
l
a
n
g
B
l
o
c
k
B
i
n
d
i
n
g
T
e
s
t
`
 
p
i
n
s
 
t
h
e
 
b
l
o
c
k
 
r
e
f
l
e
c
t
i
o
n
 
c
o
n
t
r
a
c
t
 
(
b
a
r
e
 
m
e
m
b
e
r
n
a
m
e
s
,
 
c
o
m
p
i
l
e
r
-
a
s
s
i
g
n
e
d
 
o
r
d
e
r
,
 
m
u
l
t
i
-
b
l
o
c
k
 
s
e
t
s
)
.
 
R
e
n
d
e
r
i
n
g
 
a
n
d
 
W
o
r
l
d
3
D
v
a
l
i
d
a
t
i
o
n
 
c
o
m
p
i
l
e
 
e
v
e
r
y
 
e
n
t
r
y
-
p
o
i
n
t
 
m
o
d
u
l
e
 
a
n
d
 
i
n
s
p
e
c
t
 
S
l
a
n
g
 
r
e
f
l
e
c
t
i
o
n
h
e
a
d
l
e
s
s
l
y
-
 
A
 
s
u
r
f
a
c
e
 
d
e
c
l
a
r
e
s
 
i
t
s
 
r
e
s
o
u
r
c
e
s
 
i
n
 
i
t
s
 
o
w
n
 
`
P
a
r
a
m
e
t
e
r
B
l
o
c
k
`
 
(
a
n
y
 
b
l
o
c
k
 
 
n
a
m
e
;
 
t
h
e
 
m
a
t
e
r
i
a
l
 
s
e
t
 
i
s
 
w
h
e
r
e
v
e
r
 
t
h
e
 
c
o
m
p
o
s
e
d
 
l
a
y
o
u
t
 
p
u
t
s
 
i
t
 
—
 
t
h
e
 
e
n
g
i
n
e
 
 
r
e
a
d
s
 
t
e
x
t
u
r
e
 
s
l
o
t
s
 
f
r
o
m
 
t
h
e
 
s
u
r
f
a
c
e
 
m
o
d
u
l
e
'
s
 
r
e
f
l
e
c
t
i
o
n
,
 
n
o
t
 
a
 
s
e
t
 
n
u
m
b
e
r
)
.
 
 
T
h
e
 
e
n
g
i
n
e
 
b
i
n
d
s
 
m
e
m
b
e
r
s
 
b
y
 
b
a
r
e
 
n
a
m
e
.
-
 
O
n
e
 
b
l
o
c
k
 
=
 
o
n
e
 
w
h
o
l
e
 
d
e
s
c
r
i
p
t
o
r
 
s
e
t
;
 
b
l
o
c
k
s
 
t
a
k
e
 
s
e
t
s
 
i
n
 
d
e
c
l
a
r
a
t
i
o
n
 
o
r
d
e
r
 
 
(
e
n
t
r
y
 
m
o
d
u
l
e
 
b
l
o
c
k
s
 
f
i
r
s
t
,
 
t
h
e
n
 
c
o
m
p
a
n
i
o
n
 
m
o
d
u
l
e
s
'
,
 
t
h
e
n
 
i
m
p
o
r
t
e
d
 
m
o
d
u
l
e
s
'
)
.
 
 
B
a
r
e
 
g
l
o
b
a
l
s
 
(
n
o
 
b
l
o
c
k
)
 
f
i
l
l
 
s
e
t
 
0
 
b
e
f
o
r
e
 
a
n
y
 
b
l
o
c
k
 
—
 
n
e
w
 
c
o
d
e
 
s
h
o
u
l
d
 
n
o
t
 
m
i
x
 
 
t
h
e
m
;
 
w
r
a
p
 
g
l
o
b
a
l
s
 
i
n
 
a
 
b
l
o
c
k
.
-
 
A
 
b
l
o
c
k
 
w
i
t
h
 
o
r
d
i
n
a
r
y
 
d
a
t
a
 
g
e
t
s
 
a
n
 
a
u
t
o
m
a
t
i
c
a
l
l
y
-
i
n
t
r
o
d
u
c
e
d
 
u
n
i
f
o
r
m
 
b
u
f
f
e
r
 
a
t
 
 
b
i
n
d
i
n
g
 
0
 
u
n
d
e
r
 
t
h
e
 
b
l
o
c
k
 
v
a
r
i
a
b
l
e
'
s
 
n
a
m
e
;
 
r
e
s
o
u
r
c
e
 
m
e
m
b
e
r
s
 
c
o
n
t
i
n
u
e
 
a
f
t
e
r
 
i
t
 
 
i
n
 
d
e
c
l
a
r
a
t
i
o
n
 
o
r
d
e
r
.
 
A
 
r
e
s
o
u
r
c
e
-
o
n
l
y
 
b
l
o
c
k
 
e
m
i
t
s
 
n
o
 
U
B
O
 
a
n
d
 
i
t
s
 
m
e
m
b
e
r
s
 
s
t
a
r
t
 
 
a
t
 
b
i
n
d
i
n
g
 
0
.
-
 
B
l
o
c
k
s
 
a
n
d
 
t
h
e
i
r
 
s
t
r
u
c
t
s
 
n
e
e
d
 
`
p
u
b
l
i
c
`
 
m
e
m
b
e
r
s
 
w
h
e
n
 
s
h
a
r
e
d
 
a
c
r
o
s
s
 
m
o
d
u
l
e
s
 
 
(
S
l
a
n
g
 
2
0
2
5
 
v
i
s
i
b
i
l
i
t
y
 
i
s
 
i
n
t
e
r
n
a
l
 
b
y
 
d
e
f
a
u
l
t
)
.
-
 
B
l
o
c
k
 
m
e
m
b
e
r
s
 
k
e
e
p
 
t
h
e
i
r
 
b
a
r
e
 
f
i
e
l
d
 
n
a
m
e
s
 
i
n
 
r
e
f
l
e
c
t
i
o
n
 
(
`
_
o
u
t
p
u
t
`
,
 
n
o
t
 
 
`
_
p
a
s
s
.
_
o
u
t
p
u
t
`
)
 
—
 
t
h
e
 
s
h
a
d
e
r
 
b
o
d
y
 
q
u
a
l
i
f
i
e
s
 
(
`
_
p
a
s
s
.
_
o
u
t
p
u
t
`
)
,
 
b
u
t
 
t
h
e
 
 
e
n
g
i
n
e
 
b
i
n
d
s
 
b
y
 
t
h
e
 
b
a
r
e
 
n
a
m
e
;
 
b
i
n
d
i
n
g
 
n
u
m
b
e
r
s
 
a
r
e
 
p
r
i
v
a
t
e
 
p
h
y
s
i
c
a
l
 
l
a
y
o
u
t
 
 
a
n
d
 
m
u
s
t
 
n
o
t
 
a
p
p
e
a
r
 
i
n
 
c
a
l
l
e
r
 
l
o
g
i
c
.
W
h
e
n
 
a
 
p
a
s
s
 
n
e
e
d
s
 
a
n
 
u
n
f
i
l
t
e
r
e
d
 
r
a
w
 
d
e
p
t
h
 
v
a
l
u
e
,
 
d
e
c
l
a
r
e
 
`
D
e
p
t
h
T
e
x
t
u
r
e
2
D
`
,
 
c
a
l
l
`
L
o
a
d
`
,
 
a
n
d
 
b
i
n
d
 
t
h
e
 
f
r
a
m
e
b
u
f
f
e
r
'
s
 
n
a
t
i
v
e
 
d
e
p
t
h
 
a
t
t
a
c
h
m
e
n
t
 
w
i
t
h
`
S
e
t
R
e
n
d
e
r
T
e
x
t
u
r
e
D
e
p
t
h
`
.
 
D
o
 
n
o
t
 
a
d
d
 
a
 
c
o
l
o
r
 
d
e
p
t
h
 
m
i
r
r
o
r
 
o
r
 
a
n
 
e
x
p
l
i
c
i
t
l
y
f
o
r
m
a
t
t
e
d
 
`
T
e
x
t
u
r
e
2
D
<
f
l
o
a
t
>
`
 
s
u
b
s
t
i
t
u
t
e
.
D
i
r
e
c
t
 
S
P
I
R
-
V
 
c
o
m
p
i
l
e
r
 
o
u
t
p
u
t
 
i
s
 
m
a
n
d
a
t
o
r
y
.
 
D
o
 
n
o
t
 
s
e
l
e
c
t
 
a
 
c
o
m
p
i
l
e
r
 
b
a
c
k
e
n
d
 
i
n
s
h
a
d
e
r
-
l
o
a
d
i
n
g
 
c
o
d
e
 
o
r
 
i
n
t
r
o
d
u
c
e
 
a
 
v
i
a
-
G
L
S
L
/
g
l
s
l
a
n
g
 
f
a
l
l
b
a
c
k
.
 
O
n
 
V
u
l
k
a
n
,
w
g
p
u
-
n
a
t
i
v
e
'
s
 
S
P
I
R
-
V
 
p
a
s
s
t
h
r
o
u
g
h
 
c
a
p
a
b
i
l
i
t
y
 
s
u
b
m
i
t
s
 
t
h
e
 
v
a
l
i
d
a
t
e
d
 
S
l
a
n
g
 
o
u
t
p
u
t
w
i
t
h
o
u
t
 
a
 
N
a
g
a
 
i
m
p
o
r
t
/
r
e
-
e
m
i
s
s
i
o
n
 
r
o
u
n
d
 
t
r
i
p
.
 
N
o
n
-
V
u
l
k
a
n
 
b
a
c
k
e
n
d
s
 
r
e
t
a
i
n
 
w
g
p
u
'
s
n
o
r
m
a
l
 
t
a
r
g
e
t
 
t
r
a
n
s
l
a
t
i
o
n
 
p
a
t
h
.
#
#
 
V
a
l
i
d
a
t
i
o
n
B
e
f
o
r
e
 
l
a
n
d
i
n
g
 
s
h
a
d
e
r
 
c
h
a
n
g
e
s
,
 
r
u
n
:
`
`
`
t
e
x
t
d
o
t
n
e
t
 
b
u
i
l
d
 
A
l
c
o
.
s
l
n
x
 
-
-
n
o
-
r
e
s
t
o
r
e
d
o
t
n
e
t
 
t
e
s
t
 
T
e
s
t
/
A
l
c
o
.
S
h
a
d
e
r
C
o
m
p
i
l
e
r
.
T
e
s
t
/
A
l
c
o
.
S
h
a
d
e
r
C
o
m
p
i
l
e
r
.
T
e
s
t
.
c
s
p
r
o
j
 
-
-
n
o
-
b
u
i
l
d
d
o
t
n
e
t
 
t
e
s
t
 
T
e
s
t
/
A
l
c
o
.
R
e
n
d
e
r
i
n
g
.
T
e
s
t
/
A
l
c
o
.
R
e
n
d
e
r
i
n
g
.
T
e
s
t
.
c
s
p
r
o
j
 
-
-
n
o
-
b
u
i
l
d
d
o
t
n
e
t
 
t
e
s
t
 
T
e
s
t
/
A
l
c
o
.
W
o
r
l
d
3
D
.
T
e
s
t
/
A
l
c
o
.
W
o
r
l
d
3
D
.
T
e
s
t
.
c
s
p
r
o
j
 
-
-
n
o
-
b
u
i
l
d
`
`
`
`
S
l
a
n
g
S
o
u
r
c
e
C
o
n
v
e
n
t
i
o
n
T
e
s
t
`
 
e
n
f
o
r
c
e
s
 
m
o
d
u
l
e
 
h
e
a
d
e
r
s
,
 
f
i
l
e
/
m
o
d
u
l
e
 
n
a
m
i
n
g
(
k
e
b
a
b
-
c
a
s
e
 
f
i
l
e
s
 
p
a
i
r
e
d
 
w
i
t
h
 
s
n
a
k
e
_
c
a
s
e
 
m
o
d
u
l
e
 
n
a
m
e
s
)
,
 
r
e
m
o
v
e
s
 
l
e
g
a
c
y
 
H
L
S
L
a
n
d
 
r
e
j
e
c
t
s
 
t
e
x
t
u
a
l
 
i
n
c
l
u
d
e
s
,
 
a
 
`
r
e
g
i
s
t
e
r
(
.
.
.
)
`
 
d
e
c
l
a
r
a
t
i
o
n
 
w
i
t
h
o
u
t
 
a
 
s
e
t
,
`
[
[
v
k
:
:
b
i
n
d
i
n
g
]
]
`
 
d
e
c
o
r
a
t
i
o
n
s
,
 
a
n
d
 
r
e
g
i
s
t
e
r
s
 
o
u
t
s
i
d
e
 
c
b
u
f
f
e
r
/
C
o
n
s
t
a
n
t
B
u
f
f
e
r
b
l
o
c
k
s
.
 
`
S
l
a
n
g
B
l
o
c
k
B
i
n
d
i
n
g
T
e
s
t
`
 
p
i
n
s
 
t
h
e
 
b
l
o
c
k
 
r
e
f
l
e
c
t
i
o
n
 
c
o
n
t
r
a
c
t
 
(
b
a
r
e
 
m
e
m
b
e
r
n
a
m
e
s
,
 
c
o
m
p
i
l
e
r
-
a
s
s
i
g
n
e
d
 
o
r
d
e
r
,
 
m
u
l
t
i
-
b
l
o
c
k
 
s
e
t
s
)
.
 
R
e
n
d
e
r
i
n
g
 
a
n
d
 
W
o
r
l
d
3
D
v
a
l
i
d
a
t
i
o
n
 
c
o
m
p
i
l
e
 
e
v
e
r
y
 
e
n
t
r
y
-
p
o
i
n
t
 
m
o
d
u
l
e
 
a
n
d
 
i
n
s
p
e
c
t
 
S
l
a
n
g
 
r
e
f
l
e
c
t
i
o
n
h
e
a
d
l
e
s
s
l
y
.
