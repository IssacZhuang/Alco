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
`
.
 
P
r
e
f
i
x
 
s
h
a
r
e
d
 
m
o
d
u
l
e
s
 
w
i
t
h


t
h
e
i
r
 
o
w
n
i
n
g
 
a
s
s
e
m
b
l
y
 
(
`
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
`
,
 
`
a
l
c
o
_
w
o
r
l
d
3
d
_
`
)
;
 
t
h
e
 
e
n
g
i
n
e
-
s
i
d
e


l
o
a
d
 
n
a
m
e
 
i
s
 
t
h
e
 
d
a
s
h
e
d
 
s
t
e
m
,
 
s
o
 
`
B
u
i
l
t
I
n
A
s
s
e
t
s
`
 
e
x
p
o
s
e
s
 
e
a
c
h
 
p
i
p
e
l
i
n
e
 
s
h
a
d
e
r


a
s
 
`
S
h
a
d
e
r
_
`
 
+
 
t
h
e
 
s
t
e
m
 
P
a
s
c
a
l
C
a
s
e
d
 
p
e
r
 
d
a
s
h
e
d
 
w
o
r
d
 
(
`
S
h
a
d
e
r
_
G
a
u
s
s
i
a
n
B
l
u
r
R
g
b
a
1
6
f
`
)
.




U
s
e
 
`
i
m
p
o
r
t
`
;
 
`
#
i
n
c
l
u
d
e
`
 
i
s
 
f
o
r
b
i
d
d
e
n
.
 
E
x
p
o
r
t
 
o
n
l
y
 
t
h
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
s
 
a
n
o
t
h
e
r


m
o
d
u
l
e
 
c
o
n
s
u
m
e
s
.
 
U
n
d
e
r
 
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
,
 
s
o
 
s
h
a
r
e
d


t
y
p
e
s
 
a
n
d
 
f
u
n
c
t
i
o
n
s
 
m
u
s
t
 
b
e
 
m
a
r
k
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
 
d
e
l
i
b
e
r
a
t
e
l
y
.




#
#
 
I
d
e
n
t
i
f
i
e
r
 
c
a
s
i
n
g




T
y
p
e
s
 
(
s
t
r
u
c
t
,
 
i
n
t
e
r
f
a
c
e
,
 
e
n
u
m
)
 
a
r
e
 
U
p
p
e
r
C
a
m
e
l
C
a
s
e
 
(
`
S
h
a
d
o
w
V
e
r
t
e
x
`
,
 
`
I
S
u
r
f
a
c
e
`
)


f
o
l
l
o
w
i
n
g
 
t
h
e
 
o
f
f
i
c
i
a
l
 
S
l
a
n
g
 
c
o
n
v
e
n
t
i
o
n
s
.
 
V
a
l
u
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
C
a
m
e
l
C
a
s
e
,
 
w
i
t
h
 
t
w
o


d
e
l
i
b
e
r
a
t
e
 
h
o
u
s
e
-
r
u
l
e
 
e
x
c
e
p
t
i
o
n
s
 
i
n
h
e
r
i
t
e
d
 
f
r
o
m
 
t
h
e
 
H
L
S
L
 
l
i
n
e
a
g
e
:
 
e
n
t
r
y
 
p
o
i
n
t
s


a
n
d
 
i
n
t
e
r
f
a
c
e
 
m
e
t
h
o
d
s
 
s
t
a
y
 
P
a
s
c
a
l
C
a
s
e
 
(
`
M
a
i
n
V
S
`
,
 
`
G
e
t
B
a
s
e
C
o
l
o
r
`
)
 
b
e
c
a
u
s
e
 
C
#


l
o
o
k
s
 
t
h
e
m
 
u
p
 
b
y
 
n
a
m
e
,
 
a
n
d
 
m
o
d
u
l
e
-
s
c
o
p
e
 
s
h
a
d
e
r
 
r
e
s
o
u
r
c
e
s
 
k
e
e
p
 
t
h
e
 
`
_
`
 
p
r
e
f
i
x


(
`
_
t
e
x
t
u
r
e
`
,
 
`
_
i
n
s
t
a
n
c
e
s
`
)
 
t
o
 
k
e
e
p
 
t
h
e
m
 
v
i
s
u
a
l
l
y
 
d
i
s
t
i
n
c
t
 
f
r
o
m
 
l
o
c
a
l
s
.
 
S
t
a
t
i
c


c
o
n
s
t
a
n
t
s
 
a
r
e
 
S
C
R
E
A
M
I
N
G
_
S
N
A
K
E
_
C
A
S
E
 
(
`
P
I
`
,
 
`
A
L
C
O
_
G
R
O
U
P
_
F
R
A
M
E
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
 
a
r
e


w
r
i
t
t
e
n
 
a
l
l
-
u
p
p
e
r
 
o
r
 
a
l
l
-
l
o
w
e
r
,
 
n
e
v
e
r
 
t
i
t
l
e
-
c
a
s
e
d
:
 
`
i
n
s
t
a
n
c
e
I
D
`
,
 
`
n
o
r
m
a
l
T
S
`
,


n
o
t
 
`
i
n
s
t
a
n
c
e
I
d
`
.




#
#
 
E
n
t
r
y
 
p
o
i
n
t
s
 
a
n
d
 
v
a
r
i
a
n
t
s




E
n
t
r
y
 
p
o
i
n
t
s
 
a
r
e
 
n
a
m
e
d
 
`
M
a
i
n
V
S
`
,
 
`
M
a
i
n
P
S
`
 
o
r
 
`
M
a
i
n
C
S
`
 
a
n
d
 
c
a
r
r
y
 
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


s
t
a
g
e
 
a
t
t
r
i
b
u
t
e
:




`
`
`
s
l
a
n
g


[
s
h
a
d
e
r
(
"
f
r
a
g
m
e
n
t
"
)
]


f
l
o
a
t
4
 
M
a
i
n
P
S
(
V
a
r
y
i
n
g
s
 
i
n
p
u
t
)
 
:
 
S
V
_
T
A
R
G
E
T
 
{
 
/
*
 
.
.
.
 
*
/
 
}


`
`
`




U
s
e
 
`
v
e
r
t
e
x
`
,
 
`
f
r
a
g
m
e
n
t
`
 
a
n
d
 
`
c
o
m
p
u
t
e
`
;
 
d
o
 
n
o
t
 
a
d
d
 
t
h
e
 
H
L
S
L
 
a
l
i
a
s
 
`
p
i
x
e
l
`
.


C
o
m
p
u
t
e
 
e
n
t
r
y
 
p
o
i
n
t
s
 
a
l
s
o
 
d
e
c
l
a
r
e
 
`
[
n
u
m
t
h
r
e
a
d
s
(
x
,
 
y
,
 
z
)
]
`
.




V
a
r
i
a
n
t
 
a
x
e
s
 
s
p
l
i
t
 
b
y
 
o
w
n
e
r
.
 
E
n
g
i
n
e
-
o
w
n
e
d
 
v
a
r
i
a
n
t
 
a
x
e
s
 
(
f
x
a
a
 
q
u
a
l
i
t
y
,


s
R
G
B
 
c
o
m
p
r
e
s
s
i
o
n
,
 
c
l
o
u
d
-
n
o
i
s
e
 
b
a
k
e
 
k
i
n
d
,
 
t
i
l
e
-
i
n
s
t
a
n
c
e
d
 
f
a
c
a
d
e
/
b
o
m
b
i
n
g
)
 
a
r
e


g
e
n
e
r
i
c
 
v
a
l
u
e
 
p
a
r
a
m
e
t
e
r
s
:
 
t
h
e
 
e
n
t
r
y
 
p
o
i
n
t
 
d
e
c
l
a
r
e
s
 
`
<
l
e
t
 
Q
u
a
l
i
t
y
 
:
 
i
n
t
>
`
 
a
n
d


t
h
e
 
C
#
 
o
w
n
e
r
 
p
a
s
s
e
s
 
t
h
e
 
s
p
e
c
i
a
l
i
z
a
t
i
o
n
 
a
r
g
u
m
e
n
t
s
 
w
h
e
r
e
 
t
h
e
 
r
e
t
i
r
e
d
 
d
e
f
i
n
e
s


u
s
e
d
 
t
o
 
b
e
 
—
 
t
h
r
o
u
g
h
 
t
h
e
 
a
c
c
e
s
s
o
r
 
m
e
t
h
o
d
s
 
o
f
 
t
h
e
 
m
o
d
u
l
e
'
s
 
S
h
a
d
e
r
 
h
a
n
d
l
e


(
`
G
e
t
G
r
a
p
h
i
c
s
P
i
p
e
l
i
n
e
(
l
a
y
o
u
t
,
 
…
,
 
"
2
"
)
`
,
 
`
G
e
t
C
o
m
p
u
t
e
P
i
p
e
l
i
n
e
I
n
f
o
(
"
0
"
)
`
)
 
o
r
 
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
 
f
a
c
t
o
r
i
e
s
 
(
`
C
r
e
a
t
e
G
r
a
p
h
i
c
s
M
a
t
e
r
i
a
l
(
s
h
a
d
e
r
,
 
n
a
m
e
,
 
"
0
"
,
 
"
1
"
)
`
)
 
—
 
t
h
e


a
r
g
u
m
e
n
t
s
 
a
r
e
 
s
l
a
n
g
 
e
x
p
r
e
s
s
i
o
n
s
 
(
`
"
0
"
`
,
 
`
"
1
"
`
,
 
t
y
p
e
 
n
a
m
e
s
)
 
m
a
p
p
e
d
 
t
o
 
t
h
e
 
e
n
t
r
y


p
o
i
n
t
s
'
 
g
e
n
e
r
i
c
 
p
a
r
a
m
e
t
e
r
s
 
i
n
 
d
e
f
i
n
i
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
 
O
n
e
 
S
h
a
d
e
r
 
i
s
 
o
n
e
 
m
o
d
u
l
e
'
s


h
a
n
d
l
e
:
 
s
p
e
c
i
a
l
i
z
a
t
i
o
n
s
 
c
o
m
p
i
l
e
 
l
a
z
i
l
y
,
 
o
n
c
e
 
p
e
r
 
a
r
g
u
m
e
n
t
 
s
e
t
,
 
a
n
d
 
c
a
c
h
e


i
n
s
i
d
e
 
t
h
e
 
s
h
a
d
e
r
;
 
m
a
t
e
r
i
a
l
s
 
a
r
e
 
c
o
n
s
t
r
u
c
t
i
o
n
-
b
o
u
n
d
 
t
o
 
(
s
h
a
d
e
r
,


s
p
e
c
i
a
l
i
z
a
t
i
o
n
)
 
a
n
d
 
n
e
v
e
r
 
m
u
t
a
t
e
 
t
h
e
i
r
 
b
i
n
d
i
n
g
.
 
N
e
v
e
r
 
c
o
n
v
e
r
t
 
t
h
e
s
e
 
b
a
c
k
 
t
o


`
#
i
f
`
 
p
e
r
m
u
t
a
t
i
o
n
s
 
(
t
h
e
 
s
p
r
i
t
e
 
w
r
a
p
 
m
o
d
e
 
i
s
 
t
h
e
 
m
o
d
u
l
e
'
s
 
o
w
n
 
`
<
l
e
t
 
R
e
p
e
a
t
e
d
 
:


b
o
o
l
>
`
 
a
x
i
s
 
n
o
w
)
.
 
P
r
e
p
r
o
c
e
s
s
o
r
 
d
e
f
i
n
e
s
 
a
r
e


r
e
s
e
r
v
e
d
 
f
o
r
 
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
-
t
i
m
e
 
m
a
t
e
r
i
a
l
 
k
e
y
w
o
r
d
s
 
o
n
l
y
:
 
u
s
e
r
-
a
u
t
h
o
r
e
d


`
M
a
t
e
r
i
a
l
A
s
s
e
t
.
D
e
f
i
n
e
s
`
 
a
n
d
 
`
S
H
A
D
O
W
_
C
U
T
O
U
T
`
 
(
g
a
t
e
s
 
v
a
r
y
i
n
g
-
s
t
r
u
c
t
 
s
h
a
p
e
)
,


b
a
k
e
d
 
i
n
t
o
 
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
 
k
e
y
 
b
e
f
o
r
e
 
c
o
m
p
i
l
a
t
i
o
n
.
 
D
o
 
n
o
t
 
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
 
n
e
w
 
`
#
i
f
`


p
e
r
m
u
t
a
t
i
o
n
 
o
u
t
s
i
d
e
 
t
h
a
t
 
d
o
m
a
i
n
.




#
#
 
M
a
t
e
r
i
a
l
 
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
 
(
W
o
r
l
d
3
D
 
s
u
r
f
a
c
e
s
 
a
n
d
 
p
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
)




M
a
t
e
r
i
a
l
s
 
a
r
e
 
s
l
a
n
g
 
t
y
p
e
s
,
 
n
o
t
 
s
t
r
i
n
g
 
p
e
r
m
u
t
a
t
i
o
n
s
.
 
A
 
m
a
t
e
r
i
a
l
 
i
s
 
a
 
s
t
r
u
c
t


i
m
p
l
e
m
e
n
t
i
n
g
 
`
I
S
u
r
f
a
c
e
`
 
(
`
L
i
b
s
/
a
l
c
o
-
w
o
r
l
d
3
d
-
s
u
r
f
a
c
e
.
s
l
a
n
g
`
)
;
 
a
 
p
a
s
s
 
i
s
 
a


t
e
m
p
l
a
t
e
 
m
o
d
u
l
e
 
w
h
o
s
e
 
e
n
t
r
y
 
p
o
i
n
t
s
 
a
r
e
 
g
e
n
e
r
i
c
 
o
v
e
r
 
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
:




`
`
`
s
l
a
n
g


[
s
h
a
d
e
r
(
"
v
e
r
t
e
x
"
)
]
 
p
u
b
l
i
c
 
M
a
i
n
V
O
u
t
 
M
a
i
n
V
S
<
T
 
:
 
I
S
u
r
f
a
c
e
>
(
M
a
i
n
V
I
n
 
v
)
 
{
 
.
.
.
 
}


`
`
`




C
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
 
i
s
 
`
s
p
e
c
i
a
l
i
z
e
(
e
n
t
r
y
P
o
i
n
t
,
 
s
u
r
f
a
c
e
T
y
p
e
)
`
 
+
 
l
i
n
k
 
—
 
t
h
e
r
e
 
i
s
 
n
o


g
e
n
e
r
a
t
e
d
 
w
r
a
p
p
e
r
 
s
h
a
d
e
r
 
a
n
y
w
h
e
r
e
 
i
n
 
t
h
e
 
p
i
p
e
l
i
n
e
.
 
T
h
e
 
r
u
l
e
s
:




-
 
S
u
r
f
a
c
e
 
i
n
t
e
r
f
a
c
e
s
 
a
r
e
 
f
i
n
e
-
g
r
a
i
n
e
d
 
(
`
I
V
e
r
t
e
x
S
u
r
f
a
c
e
`
,
 
`
I
A
l
b
e
d
o
S
u
r
f
a
c
e
`
,


 
 
`
I
N
o
r
m
a
l
S
u
r
f
a
c
e
`
,
 
`
I
M
a
t
e
r
i
a
l
P
r
o
p
s
S
u
r
f
a
c
e
`
,
 
`
I
E
m
i
s
s
i
v
e
S
u
r
f
a
c
e
`
,


 
 
`
I
V
o
x
e
l
F
e
e
d
S
u
r
f
a
c
e
`
)
 
w
i
t
h
 
f
u
l
l
 
d
e
f
a
u
l
t
 
i
m
p
l
e
m
e
n
t
a
t
i
o
n
s
;
 
`
I
S
u
r
f
a
c
e
`


 
 
a
g
g
r
e
g
a
t
e
s
 
t
h
e
m
.
 
A
 
s
u
r
f
a
c
e
 
o
v
e
r
r
i
d
e
s
 
o
n
l
y
 
w
h
a
t
 
i
t
 
n
e
e
d
s
,
 
a
n
d
 
e
v
e
r
y


 
 
o
v
e
r
r
i
d
e
 
m
u
s
t
 
c
a
r
r
y
 
t
h
e
 
`
o
v
e
r
r
i
d
e
`
 
m
o
d
i
f
i
e
r
 
(
S
l
a
n
g
 
e
r
r
o
r
 
3
6
1
0
7


 
 
o
t
h
e
r
w
i
s
e
)
 
—
 
i
n
t
e
n
t
 
i
s
 
e
x
p
l
i
c
i
t
.


-
 
B
e
h
a
v
i
o
r
 
b
r
a
n
c
h
e
s
 
i
n
s
i
d
e
 
a
 
p
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
 
u
s
e
 
*
*
v
a
l
u
e
 
s
p
e
c
i
a
l
i
z
a
t
i
o
n
*
*


 
 
(
`
w
h
e
r
e
 
l
e
t
 
A
l
p
h
a
T
e
s
t
 
:
 
b
o
o
l
`
)
,
 
r
e
q
u
e
s
t
e
d
 
f
r
o
m
 
C
#
 
v
i
a
 
t
h
e
 
c
o
m
p
i
l
e
 
c
a
l
l
'
s


 
 
v
a
l
u
e
-
s
p
e
c
i
a
l
i
z
a
t
i
o
n
 
a
r
g
u
m
e
n
t
s
.
 
T
h
i
s
 
i
s
 
t
h
e
 
s
a
m
e
 
m
e
c
h
a
n
i
s
m
 
a
s


 
 
e
n
g
i
n
e
-
o
w
n
e
d
 
g
e
n
e
r
i
c
 
v
a
l
u
e
 
p
a
r
a
m
e
t
e
r
s
;
 
r
e
t
i
r
e
d
 
t
e
x
t
u
a
l
 
p
e
r
m
u
t
a
t
i
o
n
s


 
 
(
e
.
g
.
 
`
S
H
A
D
O
W
_
C
U
T
O
U
T
`
)
 
m
u
s
t
 
n
o
t
 
c
o
m
e
 
b
a
c
k
.


-
 
S
u
r
f
a
c
e
-
d
e
c
l
a
r
e
d
 
r
e
s
o
u
r
c
e
s
 
f
o
l
l
o
w
 
t
h
e
 
s
a
m
e
 
s
e
t
-
s
c
o
p
e
d
 
c
b
u
f
f
e
r
-
b
l
o
c
k


 
 
c
o
n
v
e
n
t
i
o
n
 
a
s
 
e
v
e
r
y
t
h
i
n
g
 
e
l
s
e
,
 
i
n
 
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
 
(
s
p
a
c
e
2
,


 
 
`
M
a
t
e
r
i
a
l
C
o
m
p
i
l
e
r
.
S
u
r
f
a
c
e
R
e
s
o
u
r
c
e
S
e
t
`
)
:
 
`
c
b
u
f
f
e
r
 
_
m
a
t
e
r
i
a
l
 
:


 
 
r
e
g
i
s
t
e
r
(
b
0
,
 
s
p
a
c
e
2
)
 
{
 
T
e
x
t
u
r
e
2
D
 
_
a
l
b
e
d
o
T
e
x
t
u
r
e
;
 
.
.
.
 
}
`
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
 
T
h
e
 
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
 
b
a
n
 
a
p
p
l
i
e
s
 
t
o


 
 
s
u
r
f
a
c
e
s
 
t
o
o
 
—
 
b
l
o
c
k
 
+
 
s
e
t
 
i
s
 
t
h
e
 
w
h
o
l
e
 
c
o
n
t
r
a
c
t
.


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
 
k
e
e
p
 
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
 
i
n
 
t
h
e
 
l
o
w
 
s
e
t
s
 
(
f
r
a
m
e
 
0
,


 
 
p
a
s
s
 
1
,
 
d
r
a
w
 
3
)
 
p
e
r
 
t
h
e
 
r
u
l
e
s
 
a
b
o
v
e
;
 
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
 
o
w
n
s
 
s
p
a
c
e
2


 
 
a
l
o
n
e
.


-
 
T
e
m
p
l
a
t
e
 
e
n
t
r
y
 
p
o
i
n
t
s
 
m
u
s
t
 
b
e
 
`
p
u
b
l
i
c
`
 
a
n
d
 
c
a
r
r
y
 
t
h
e
 
`
[
s
h
a
d
e
r
]
`
 
s
t
a
g
e


 
 
a
t
t
r
i
b
u
t
e
 
s
o
 
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
r
 
c
a
n
 
f
i
n
d
 
t
h
e
m
 
w
i
t
h
o
u
t
 
a
 
w
r
a
p
p
e
r
.




S
e
e
 
`
d
o
c
s
/
M
a
t
e
r
i
a
l
S
y
s
t
e
m
.
m
d
`
 
f
o
r
 
t
h
e
 
C
#
 
s
i
d
e
 
(
`
M
a
t
e
r
i
a
l
C
o
m
p
o
s
e
r
`
,


`
M
a
t
e
r
i
a
l
C
o
m
p
i
l
e
r
`
,
 
p
a
s
s
 
r
e
g
i
s
t
r
a
t
i
o
n
,
 
t
e
x
t
u
r
e
-
s
l
o
t
 
a
n
d
 
p
a
r
a
m
s
-
b
l
o
c
k


r
u
l
e
s
)
.




#
#
 
R
e
s
o
u
r
c
e
s
 
a
n
d
 
b
i
n
d
i
n
g
s




U
s
e
 
r
e
a
l
 
S
l
a
n
g
 
r
e
s
o
u
r
c
e
 
t
y
p
e
s
 
(
`
T
e
x
t
u
r
e
*
`
,
 
`
S
a
m
p
l
e
r
S
t
a
t
e
`
,


`
S
a
m
p
l
e
r
C
o
m
p
a
r
i
s
o
n
S
t
a
t
e
`
,
 
s
t
r
u
c
t
u
r
e
d
 
b
u
f
f
e
r
s
 
a
n
d
 
s
t
o
r
a
g
e
 
t
e
x
t
u
r
e
s
)
.
 
N
e
w
 
c
o
d
e


m
u
s
t
 
n
o
t
 
a
d
d
 
`
D
E
F
I
N
E
_
*
`
,
 
`
S
L
O
T
`
,
 
s
a
m
p
l
e
r
-
t
o
k
e
n
-
c
o
n
c
a
t
e
n
a
t
i
o
n
 
o
r
 
d
e
p
t
h
-
m
a
r
k
e
r


m
a
c
r
o
s
.




R
e
s
o
u
r
c
e
s
 
a
r
e
 
d
e
c
l
a
r
e
d
 
i
n
s
i
d
e
 
*
*
s
e
t
-
s
c
o
p
e
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
*
*
 
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
 
s
t
a
t
e
s


o
n
l
y
 
w
h
i
c
h
 
s
e
t
 
i
t
 
o
w
n
s
,
 
a
n
d
 
S
l
a
n
g
 
a
s
s
i
g
n
s
 
m
e
m
b
e
r
 
b
i
n
d
i
n
g
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
:




`
`
`
s
l
a
n
g


c
b
u
f
f
e
r
 
_
p
a
s
s
 
:
 
r
e
g
i
s
t
e
r
(
b
0
,
 
s
p
a
c
e
1
)


{


 
 
 
 
T
e
x
t
u
r
e
2
D
 
_
s
c
e
n
e
C
o
l
o
r
;
 
 
 
 
 
 
/
/
 
b
i
n
d
i
n
g
 
0
 
i
n
 
t
h
e
 
s
e
t


 
 
 
 
S
a
m
p
l
e
r
S
t
a
t
e
 
_
s
c
e
n
e
S
a
m
p
l
e
r
;
 
/
/
 
b
i
n
d
i
n
g
 
1


 
 
 
 
R
W
S
t
r
u
c
t
u
r
e
d
B
u
f
f
e
r
<
f
l
o
a
t
4
>
 
_
o
u
t
p
u
t
;
 
/
/
 
b
i
n
d
i
n
g
 
2


}
;


`
`
`




N
e
v
e
r
 
w
r
i
t
e
 
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
(
b
i
n
d
i
n
g
,
 
s
e
t
)
]
]
`
;
 
i
t
 
p
i
n
s
 
e
v
e
r
y
 
m
e
m
b
e
r
 
a
n
d
 
d
e
f
e
a
t
s


t
h
e
 
c
o
n
v
e
n
t
i
o
n
 
(
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
 
r
e
j
e
c
t
s
 
i
t
)
.
 
T
h
e
 
r
u
l
e
s
:




-
 
O
n
e
 
s
e
t
 
=
 
o
n
e
 
b
l
o
c
k
.
 
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
u
t
 
u
n
i
f
o
r
m
 
d
a
t
a
 
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
;
 
m
e
m
b
e
r
s
 
t
a
k
e


 
 
t
h
e
 
s
e
t
'
s
 
b
i
n
d
i
n
g
s
 
f
r
o
m
 
z
e
r
o
.
 
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
 
u
n
i
f
o
r
m
 
d
a
t
a
 
e
m
i
t
s
 
i
t
s
 
b
u
f
f
e
r
 
a
t


 
 
t
h
e
 
b
l
o
c
k
'
s
 
r
e
g
i
s
t
e
r
 
(
`
b
0
`
)
 
a
n
d
 
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
.


-
 
S
e
t
s
 
a
r
e
 
f
r
e
q
u
e
n
c
y
 
g
r
o
u
p
e
d
:
 
f
r
a
m
e
 
0
,
 
p
a
s
s
 
1
,
 
m
a
t
e
r
i
a
l
 
2
,
 
d
r
a
w
 
3
 
(
W
o
r
l
d
3
D


 
 
p
r
o
g
r
a
m
s
 
l
a
y
e
r
:
 
c
o
m
m
o
n
 
m
o
d
u
l
e
s
 
o
w
n
 
t
h
e
 
l
o
w
 
s
e
t
s
,
 
t
h
e
 
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
'
s
 
o
w
n


 
 
r
e
s
o
u
r
c
e
s
 
t
a
k
e
 
t
h
e
 
f
i
r
s
t
 
f
r
e
e
 
s
e
t
 
—
 
e
a
c
h
 
s
e
t
 
b
e
l
o
n
g
s
 
t
o
 
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
)
.


-
 
P
u
r
e
 
U
B
O
 
b
l
o
c
k
s
 
s
h
a
r
i
n
g
 
o
n
e
 
s
e
t
 
u
s
e
 
s
e
q
u
e
n
t
i
a
l
 
r
e
g
i
s
t
e
r
s
 
(
`
b
0
`
,
 
`
b
1
`
,
 
…
)
;


 
 
i
f
 
a
 
m
i
x
e
d
 
p
a
r
a
m
e
t
e
r
s
+
r
e
s
o
u
r
c
e
s
 
b
l
o
c
k
 
s
h
a
r
e
s
 
t
h
e
 
s
e
t
,
 
i
t
 
c
o
m
e
s
 
l
a
s
t
 
s
o
 
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
 
r
u
n
 
p
a
s
t
 
t
h
e
 
U
B
O
s
 
(
s
e
e
 
t
h
e
 
p
a
r
a
m
e
t
e
r
i
z
e
d
-
s
u
r
f
a
c
e
 
t
e
s
t
 
f
i
x
t
u
r
e
)
.
 
A


 
 
r
e
g
i
s
t
e
r
 
o
n
 
a
 
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
 
i
s
 
i
g
n
o
r
e
d
 
b
y
 
S
l
a
n
g
 
—
 
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
s


 
 
a
l
w
a
y
s
 
o
w
n
 
t
h
e
i
r
 
s
e
t
 
a
l
o
n
e
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
 
a
n
d
 
e
v
e
r
y
 
C
#
 
c
a
l
l
 
s
i
t
e
 
a
d
d
r
e
s
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


 
 
b
y
 
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




U
s
e
 
t
h
e
 
a
c
t
u
a
l
 
d
e
p
t
h
 
t
e
x
t
u
r
e
 
a
n
d
 
c
o
m
p
a
r
i
s
o
n
-
s
a
m
p
l
e
r
 
t
y
p
e
s
.
 
D
o
 
n
o
t
 
a
d
d
 
S
P
I
R
-
V


r
e
w
r
i
t
e
r
s
,
 
s
o
u
r
c
e
 
r
e
g
e
x
 
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
 
i
m
p
l
i
c
i
t
 
s
t
r
u
c
t
u
r
e
d
-
b
u
f
f
e
r
 
c
o
u
n
t
e
r
 
n
a
m
i
n
g


r
u
l
e
s
 
o
r
 
b
i
n
d
i
n
g
 
r
e
m
a
p
p
e
r
s
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

