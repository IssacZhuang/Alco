#
 
N
a
t
i
v
e
 
S
l
a
n
g
 
M
i
g
r
a
t
i
o
n
 
P
l
a
n




S
t
a
t
u
s
:
 
i
m
p
l
e
m
e
n
t
e
d
.
 
S
u
p
e
r
s
e
d
e
s
 
t
h
e
 
W
o
r
l
d
3
D
 
s
l
a
n
g
 
p
r
o
o
f
-
o
f
-
c
o
n
c
e
p
t
 
i
n
t
e
g
r
a
t
i
o
n
.




I
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
 
n
o
t
e
:
 
t
h
e
 
c
o
m
p
l
e
t
e
d
 
r
u
n
t
i
m
e
 
f
i
r
s
t
 
u
s
e
d
 
s
o
u
r
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
 
p
o
s
i
t
i
o
n
s
 
f
o
r
 
d
e
t
e
r
m
i
n
i
s
t
i
c
 
W
e
b
G
P
U
 
l
a
y
o
u
t
s


w
h
i
l
e
 
p
r
e
s
e
r
v
i
n
g
 
t
h
e
 
n
a
m
e
-
b
a
s
e
d
 
C
#
 
c
o
n
t
r
a
c
t
 
—
 
a
t
 
t
h
e
 
t
i
m
e
,
 
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


t
o
o
l
c
h
a
i
n
 
c
o
u
l
d
 
n
o
t
 
e
x
p
r
e
s
s
 
t
h
e
 
p
l
a
n
'
s
 
s
e
t
-
o
n
l
y
 
d
e
s
i
g
n
 
w
i
t
h
o
u
t
 
a
 
r
e
m
a
p
p
i
n
g


p
a
s
s
.
 
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
 
l
i
f
t
e
d
 
t
h
a
t
 
l
i
m
i
t
:
 
r
e
s
o
u
r
c
e
s
 
n
o
w
 
l
i
v
e
 
i
n
 
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
 
(
`
c
b
u
f
f
e
r
 
_
n
a
m
e
 
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
N
)
 
{
 
.
.
.
 
}
`
)
,
 
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
f
l
e
c
t
i
o
n
 
b
r
i
d
g
e
 
e
n
u
m
e
r
a
t
e
s
 
b
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
,
 
a
n
d
 
e
x
p
l
i
c
i
t


`
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
 
a
r
e
 
g
o
n
e
 
f
r
o
m
 
e
v
e
r
y
 
s
h
a
d
e
r
 
s
o
u
r
c
e
 
(
s
e
e


`
S
h
a
d
e
r
_
B
i
n
d
i
n
g
_
S
l
o
t
_
C
o
l
l
i
s
i
o
n
s
.
m
d
`
 
a
n
d
 
`
S
l
a
n
g
C
o
d
i
n
g
S
t
a
n
d
a
r
d
.
m
d
`
)
.




S
l
a
n
g
'
s
 
d
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
 
e
m
i
t
t
e
r
 
i
s
 
t
h
e
 
o
n
l
y
 
b
a
c
k
e
n
d
.
 
T
h
e
 
c
o
m
p
i
l
e
r
 
p
i
n
s
 
t
h
e


`
s
p
i
r
v
_
1
_
3
`
 
t
a
r
g
e
t
 
p
r
o
f
i
l
e
 
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
,
 
t
h
e
 
p
u
b
l
i
c
 
o
p
t
i
o
n
s
 
n
o
 
l
o
n
g
e
r
 
e
x
p
o
s
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
 
s
w
i
t
c
h
,
 
`
S
p
i
r
v
C
o
m
p
a
t
.
c
s
`
 
i
s
 
d
e
l
e
t
e
d
,
 
a
n
d
 
g
l
s
l
a
n
g
 
i
s
 
e
x
c
l
u
d
e
d
 
f
r
o
m


r
u
n
t
i
m
e
 
o
u
t
p
u
t
s
.
 
G
e
n
e
r
a
t
e
d
 
G
B
u
f
f
e
r
/
S
h
a
d
o
w
/
R
S
M
/
G
l
a
s
s
 
m
a
t
e
r
i
a
l
 
w
r
a
p
p
e
r
s
 
a
l
l
 
u
s
e


t
h
e
 
s
a
m
e
 
m
o
d
u
l
e
 
s
y
s
t
e
m
,
 
s
o
 
a
s
s
e
t
/
t
e
m
p
l
a
t
e
 
s
e
l
e
c
t
i
o
n
 
c
a
n
n
o
t
 
c
h
a
n
g
e
 
t
h
e
 
b
a
c
k
e
n
d
.


R
a
w
 
s
c
e
n
e
 
a
n
d
 
R
S
M
 
d
e
p
t
h
 
r
e
a
d
s
 
u
s
e
 
n
a
t
i
v
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
.
L
o
a
d
`
;
 
t
h
e
 
a
c
t
u
a
l


`
D
e
p
t
h
3
2
F
l
o
a
t
`
 
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
 
i
s
 
b
o
u
n
d
 
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


T
h
e
 
t
e
m
p
o
r
a
r
y
 
`
R
3
2
F
l
o
a
t
`
 
c
o
l
o
r
 
m
i
r
r
o
r
s
 
a
n
d
 
t
h
e
i
r
 
f
r
a
g
m
e
n
t
 
o
u
t
p
u
t
s
 
a
r
e
 
r
e
m
o
v
e
d
.


N
o
 
S
P
I
R
-
V
 
b
i
n
a
r
y
 
r
e
w
r
i
t
e
r
 
o
r
 
d
e
p
t
h
 
p
a
t
c
h
e
r
 
r
e
m
a
i
n
s
.




R
u
n
t
i
m
e
 
b
i
s
e
c
t
i
o
n
 
p
r
o
v
e
d
 
t
h
a
t
 
t
h
e
 
r
e
m
a
i
n
i
n
g
 
N
V
I
D
I
A
/
V
u
l
k
a
n
 
d
e
v
i
c
e
 
l
o
s
s
 
w
a
s
 
n
o
t


i
n
v
a
l
i
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
:
 
t
h
e
 
s
a
m
e
 
`
s
p
i
r
v
-
v
a
l
`
-
c
l
e
a
n
 
b
y
t
e
s
,
 
i
n
c
l
u
d
i
n
g
 
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


l
o
a
d
s
 
a
n
d
 
t
h
e
 
r
e
n
d
e
r
e
r
'
s
 
o
r
i
g
i
n
a
l
 
l
o
o
p
 
c
o
n
t
r
o
l
 
f
l
o
w
,
 
r
u
n
 
r
e
l
i
a
b
l
y
 
w
h
e
n
 
s
u
b
m
i
t
t
e
d


t
h
r
o
u
g
h
 
w
g
p
u
'
s
 
V
u
l
k
a
n
 
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
 
A
P
I
.
 
T
h
e
 
f
a
i
l
u
r
e
 
o
c
c
u
r
s
 
o
n
l
y
 
a
f
t
e
r
 
t
h
e


S
P
I
R
-
V
 
i
s
 
i
m
p
o
r
t
e
d
 
a
n
d
 
r
e
-
e
m
i
t
t
e
d
 
b
y
 
N
a
g
a
.
 
T
h
i
s
 
a
r
e
a
 
h
a
s
 
r
e
q
u
i
r
e
d
 
e
x
p
l
i
c
i
t


d
e
p
t
h
-
r
e
s
u
l
t
 
s
h
a
p
e
 
h
a
n
d
l
i
n
g
 
u
p
s
t
r
e
a
m
 
(
w
g
p
u


[
#
4
5
5
1
]
(
h
t
t
p
s
:
/
/
g
i
t
h
u
b
.
c
o
m
/
g
f
x
-
r
s
/
w
g
p
u
/
i
s
s
u
e
s
/
4
5
5
1
)
,
 
f
i
x
e
d
 
b
y


[
#
6
3
8
4
]
(
h
t
t
p
s
:
/
/
g
i
t
h
u
b
.
c
o
m
/
g
f
x
-
r
s
/
w
g
p
u
/
p
u
l
l
/
6
3
8
4
)
)
;
 
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
 
r
e
s
u
l
t
 
h
e
r
e


d
o
e
s
 
n
o
t
 
d
e
p
e
n
d
 
o
n
 
a
n
o
t
h
e
r
 
t
r
a
n
s
l
a
t
o
r
 
p
a
s
s
.




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
 
2
9
.
0
.
1
.
1
 
e
x
p
o
r
t
s
 
`
w
g
p
u
D
e
v
i
c
e
C
r
e
a
t
e
S
h
a
d
e
r
M
o
d
u
l
e
S
p
i
r
V
`
,
 
b
u
t
 
i
t
s
 
C
 
A
P
I


l
e
a
v
e
s
 
t
h
e
 
r
e
q
u
i
r
e
d
 
`
P
A
S
S
T
H
R
O
U
G
H
_
S
H
A
D
E
R
S
`
 
f
e
a
t
u
r
e
 
m
a
p
p
i
n
g
 
c
o
m
m
e
n
t
e
d
 
o
u
t
.
 
T
h
e


p
i
n
n
e
d
 
n
a
t
i
v
e
 
p
a
t
c
h
 
e
x
p
o
s
e
s
 
t
h
a
t
 
e
x
i
s
t
i
n
g
 
w
g
p
u
-
c
o
r
e
 
f
e
a
t
u
r
e
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
 
r
e
q
u
e
s
t
s


i
t
 
w
h
e
n
 
a
d
v
e
r
t
i
s
e
d
 
a
n
d
 
s
u
b
m
i
t
s
 
S
l
a
n
g
'
s
 
w
o
r
d
s
 
u
n
c
h
a
n
g
e
d
 
o
n
 
V
u
l
k
a
n
.
 
P
a
t
c
h
e
d


w
i
n
-
x
6
4
 
a
n
d
 
l
i
n
u
x
-
x
6
4
 
r
u
n
t
i
m
e
s
 
a
r
e
 
b
u
n
d
l
e
d
.
 
O
t
h
e
r
 
r
u
n
t
i
m
e
 
i
d
e
n
t
i
f
i
e
r
s
 
d
e
t
e
c
t
 
t
h
e


m
i
s
s
i
n
g
 
f
e
a
t
u
r
e
 
a
n
d
 
s
a
f
e
l
y
 
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




F
i
n
a
l
 
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
 
(
2
0
2
6
-
0
8
-
2
3
)
:
 
a
l
l
 
9
2
 
S
l
a
n
g
 
s
o
u
r
c
e
s
 
c
a
r
r
y
 
t
h
e
 
2
0
2
5
 
l
a
n
g
u
a
g
e


p
i
n
;
 
n
o
 
`
.
h
l
s
l
`
/
`
.
h
l
s
l
i
`
,
 
D
X
C
/
D
X
I
L
 
n
a
t
i
v
e
 
b
i
n
a
r
y
,
 
l
e
g
a
c
y
 
c
o
m
p
i
l
e
r
,
 
c
u
s
t
o
m


S
P
I
R
-
V
 
r
e
f
l
e
c
t
o
r
,
 
o
r
 
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
i
n
g
 
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
 
r
e
m
a
i
n
s
.
 
`
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
`
 
c
o
m
p
l
e
t
e
s
 
w
i
t
h
 
z
e
r
o
 
e
r
r
o
r
s
 
a
n
d
 
t
h
e
 
f
u
l
l
 
s
o
l
u
t
i
o
n
 
t
e
s
t
 
r
u
n
 
p
a
s
s
e
s


9
5
1
/
9
5
1
 
t
e
s
t
s
 
a
c
r
o
s
s
 
1
1
 
t
e
s
t
 
a
s
s
e
m
b
l
i
e
s
.
 
S
a
n
d
b
o
x
 
3
4
'
s
 
c
o
m
p
l
e
t
e
 
p
r
o
c
e
d
u
r
a
l
 
P
B
R


p
i
p
e
l
i
n
e
 
a
n
d
 
r
e
s
t
o
r
e
d
 
B
i
s
t
r
o
 
s
c
e
n
e
 
(
1
,
5
9
1
 
d
r
a
w
 
i
t
e
m
s
,
 
1
3
3
 
m
a
t
e
r
i
a
l
s
,
 
4
0
5


s
t
r
e
a
m
e
d
 
t
e
x
t
u
r
e
 
i
m
a
g
e
s
)
 
e
a
c
h
 
r
a
n
 
6
0
 
f
r
a
m
e
s
 
o
n
 
a
n
 
N
V
I
D
I
A
 
R
T
X
 
4
0
7
0
 
T
i
 
t
h
r
o
u
g
h


V
u
l
k
a
n
 
w
i
t
h
 
H
B
A
O
,
 
v
o
l
u
m
e
t
r
i
c
 
c
l
o
u
d
s
/
l
i
g
h
t
,
 
V
o
x
e
l
 
G
I
/
R
S
M
,
 
S
S
R
 
a
n
d
 
B
l
o
o
m
 
e
n
a
b
l
e
d
,


t
h
e
n
 
s
h
u
t
 
d
o
w
n
 
w
i
t
h
o
u
t
 
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
 
e
r
r
o
r
s
 
o
r
 
d
e
v
i
c
e
 
l
o
s
s
.




P
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
 
c
l
e
a
n
u
p
 
(
2
0
2
6
-
0
8
-
2
4
,
 
D
3
 
f
o
l
l
o
w
-
u
p
)
:
 
d
e
a
d
 
`
#
i
f
`
 
b
r
a
n
c
h
e
s


(
`
A
L
P
H
A
_
T
E
S
T
`
,
 
p
a
r
t
i
c
l
e
/
w
a
t
e
r
 
`
I
S
_
F
A
C
A
D
E
`
,
 
w
a
t
e
r
 
`
T
E
X
T
U
R
E
_
B
O
M
B
I
N
G
`
)
 
w
e
r
e


d
e
l
e
t
e
d
,
 
a
n
d
 
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
 
b
e
c
a
m
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
 
r
e
q
u
e
s
t
e
d
 
t
h
r
o
u
g
h
 
`
S
h
a
d
e
r
S
y
s
t
e
m
.
G
e
t
S
h
a
d
e
r
(
m
o
d
u
l
e
,
 
a
r
g
s
)
`
 
—


f
x
a
a
 
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
>
`
 
(
4
 
p
r
e
s
e
t
s
)
,
 
v
o
l
u
m
e
t
r
i
c
-
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
 
`
<
l
e
t
 
I
s
D
e
t
a
i
l
>
`
,


t
e
x
t
u
r
e
-
c
o
m
p
r
e
s
s
-
b
c
3
 
`
<
l
e
t
 
I
s
S
R
G
B
>
`
;
 
t
h
e
 
`
#
i
f
n
d
e
f
`
 
d
e
f
a
u
l
t
 
g
u
a
r
d
s
 
(
H
B
A
O
,


v
o
l
u
m
e
t
r
i
c
 
l
i
g
h
t
)
 
b
e
c
a
m
e
 
`
s
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
`
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
 
n
o
w
 
s
e
r
v
e
 
o
n
l
y


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
-
k
e
y
w
o
r
d
 
d
o
m
a
i
n
:
 
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
,
 
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
 
a
n
d
 
`
R
E
P
E
A
T
E
D
`
.
 
G
e
n
e
r
i
c
 
m
o
d
u
l
e
s
 
c
a
n
n
o
t
 
l
i
n
k


u
n
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
d
,
 
s
o
 
h
e
a
d
l
e
s
s
 
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
v
e
r
s
 
t
h
e
m
 
t
h
r
o
u
g
h
 
p
e
r
-
m
o
d
u
l
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
 
t
a
b
l
e
s
 
i
n
s
t
e
a
d
 
o
f
 
t
h
e
 
n
o
-
a
r
g
u
m
e
n
t
 
a
s
s
e
t
-
l
o
a
d
 
s
w
e
e
p
.




D
e
f
i
n
e
s
 
r
e
t
i
r
e
m
e
n
t
 
(
2
0
2
6
-
0
8
-
2
4
,
 
D
3
 
c
o
m
p
l
e
t
i
o
n
)
:
 
t
h
e
 
l
a
s
t
 
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
 
d
e
f
i
n
e


a
x
e
s
 
m
o
v
e
d
 
t
o
 
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
 
—
 
s
p
r
i
t
e
'
s
 
`
R
E
P
E
A
T
E
D
`
 
b
e
c
a
m
e
 
t
h
e
 
s
p
r
i
t
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
,
 
a
n
d
 
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
'
s
 
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
 
t
o
g
g
l
e
s


b
e
c
a
m
e


`
V
e
r
t
e
x
M
a
i
n
<
l
e
t
 
I
s
F
a
c
a
d
e
>
`
 
/
 
`
P
i
x
e
l
M
a
i
n
<
l
e
t
 
B
o
m
b
i
n
g
>
`
 
(
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
s
 
m
a
p


t
o
 
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
)
.
 
A
 
S
h
a
d
e
r
 
i
s
 
n
o
w
 
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
 
i
t
s


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
 
t
a
k
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
 
s
t
a
t
e
s
,
 
"
2
"
)
`
)
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
,
 
a
n
d
 
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
 
—
 
n
o
 
r
u
n
t
i
m
e
 
r
e
b
i
n
d
i
n
g
 
s
u
r
f
a
c
e


a
n
d
 
n
o
 
p
e
r
-
m
a
t
e
r
i
a
l
 
d
e
f
i
n
e
s
 
s
t
a
t
e
.
 
T
h
e
 
r
u
n
t
i
m
e
 
d
e
f
i
n
e
s
 
A
P
I


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
.
S
e
t
D
e
f
i
n
e
s
`
,
 
`
S
h
a
d
e
r
.
G
e
t
S
h
a
d
e
r
M
o
d
u
l
e
s
(
d
e
f
i
n
e
s
)
`
,
 
`
T
e
s
t
A
l
l
D
e
f
i
n
e
s
`
,


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
 
p
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
)
 
i
s
 
d
e
l
e
t
e
d
.
 
D
e
f
i
n
e
s
 
r
e
m
a
i
n
 
o
n
l
y
 
a
s


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
 
c
o
n
s
t
a
n
t
s
 
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
,


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
,
 
w
h
e
r
e
 
t
h
e
y
 
s
e
l
e
c
t
 
w
h
o
l
e
-
m
o
d
u
l
e
 
t
e
x
t
 
s
h
a
p
e
 
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




#
#
 
1
.
 
B
a
c
k
g
r
o
u
n
d




#
#
#
 
1
.
1
 
C
u
r
r
e
n
t
 
d
x
c
-
b
a
s
e
d
 
p
i
p
e
l
i
n
e
 
(
a
s
-
b
u
i
l
t
)




-
 
*
*
C
o
m
p
i
l
e
r
*
*
:
 
d
x
c
 
r
u
n
s
 
i
n
-
p
r
o
c
e
s
s
 
v
i
a
 
h
a
n
d
-
r
o
l
l
e
d
 
C
O
M
 
v
t
a
b
l
e
 
P
/
I
n
v
o
k
e


 
 
(
`
S
r
c
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
/
B
i
n
d
i
n
g
/
D
x
c
/
`
,
 
`
D
X
C
N
a
t
i
v
e
.
c
s
`
 
→
 
`
D
x
c
C
r
e
a
t
e
I
n
s
t
a
n
c
e
`
,


 
 
`
I
D
x
c
C
o
m
p
i
l
e
r
3
:
:
C
o
m
p
i
l
e
`
)
.
 
O
n
l
y
 
`
D
x
c
O
u
t
K
i
n
d
.
O
b
j
e
c
t
`
 
a
n
d
 
`
E
r
r
o
r
s
`
 
a
r
e
 
e
x
t
r
a
c
t
e
d
;


 
 
d
x
c
'
s
 
o
w
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
 
o
u
t
p
u
t
 
i
s
 
n
e
v
e
r
 
u
s
e
d
.
 
N
a
t
i
v
e
 
b
i
n
a
r
i
e
s
 
s
h
i
p
 
i
n


 
 
`
S
r
c
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
/
r
u
n
t
i
m
e
s
/
<
r
i
d
>
/
n
a
t
i
v
e
/
`
 
(
`
d
x
c
o
m
p
i
l
e
r
.
d
l
l
`
,
 
`
d
x
i
l
.
d
l
l
`
)
.


 
 
T
h
e
 
o
n
l
y
 
r
u
n
t
i
m
e
 
b
a
c
k
e
n
d
 
i
s
 
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
 
(
W
e
b
G
P
U
)
 
c
o
n
s
u
m
i
n
g
 
S
P
I
R
-
V
;
 
t
h
e
r
e
 
i
s
 
n
o


 
 
D
3
D
1
2
/
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
.


-
 
*
*
I
n
c
l
u
d
e
s
*
*
:
 
`
S
r
c
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
/
S
h
a
d
e
r
/
I
n
c
l
u
d
e
H
e
l
p
e
r
.
c
s
`
 
f
l
a
t
t
e
n
s
 
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


 
 
r
e
c
u
r
s
i
v
e
l
y
 
i
n
t
o
 
a
 
s
i
n
g
l
e
 
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
 
u
n
i
t
 
(
m
a
x
 
d
e
p
t
h
 
3
2
,
 
`
#
l
i
n
e
`
 
m
a
r
k
e
r
s
)
.
 
T
h
e


 
 
p
l
u
m
b
e
d
 
`
I
D
x
c
I
n
c
l
u
d
e
H
a
n
d
l
e
r
`
 
p
a
t
h
 
i
s
 
e
f
f
e
c
t
i
v
e
l
y
 
u
n
u
s
e
d
.


-
 
*
*
B
i
n
d
i
n
g
s
*
*
:
 
`
S
r
c
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
/
A
s
s
e
t
s
/
S
h
a
d
e
r
s
/
L
i
b
s
/
C
o
r
e
.
h
l
s
l
i
`
 
d
e
f
i
n
e
s


 
 
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
 
m
a
c
r
o
s
 
e
x
p
a
n
d
i
n
g
 
t
o
 
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
s
p
a
c
e
N
)
`
 
w
i
t
h
 
n
o
 
r
e
g
i
s
t
e
r
 
n
u
m
b
e
r
;
 
d
x
c


 
 
a
u
t
o
-
a
s
s
i
g
n
s
 
b
i
n
d
i
n
g
s
 
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
l
y
 
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
.
 
C
#
 
r
e
s
o
l
v
e
s


 
 
e
v
e
r
y
 
r
e
s
o
u
r
c
e
 
*
*
b
y
 
n
a
m
e
,
 
n
e
v
e
r
 
b
y
 
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
*
*


 
 
(
`
d
o
c
s
/
S
h
a
d
e
r
_
B
i
n
d
i
n
g
_
S
l
o
t
_
C
o
l
l
i
s
i
o
n
s
.
m
d
`
)
.
 
C
o
m
p
i
l
e
 
f
l
a
g
s
 
r
e
l
y
 
o
n


 
 
`
-
f
s
p
v
-
p
r
e
s
e
r
v
e
-
i
n
t
e
r
f
a
c
e
 
-
f
s
p
v
-
p
r
e
s
e
r
v
e
-
b
i
n
d
i
n
g
s
`
 
s
o
 
u
n
u
s
e
d
-
b
u
t
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
 
k
e
e
p
 
t
h
e
i
r
 
s
l
o
t
s
.


-
 
*
*
R
e
f
l
e
c
t
i
o
n
*
*
:
 
a
 
c
u
s
t
o
m
 
S
P
I
R
-
V
 
p
a
r
s
e
r
 
(
`
S
r
c
/
A
l
c
o
.
G
r
a
p
h
i
c
s
/
S
p
i
r
v
/
S
p
i
r
v
R
e
f
l
e
c
t
o
r
.
c
s
`
)


 
 
r
e
-
d
e
r
i
v
e
s
 
`
S
h
a
d
e
r
R
e
f
l
e
c
t
i
o
n
I
n
f
o
`
 
(
b
i
n
d
 
g
r
o
u
p
 
l
a
y
o
u
t
s
,
 
v
e
r
t
e
x
 
i
n
p
u
t
,
 
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
 
t
h
r
e
a
d
 
g
r
o
u
p
 
s
i
z
e
)
 
f
r
o
m
 
c
o
m
p
i
l
e
d
 
S
P
I
R
-
V
.
 
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
 
d
i
s
c
o
v
e
r
e
d


 
 
b
y
 
r
e
g
e
x
i
n
g
 
t
h
e
 
s
o
u
r
c
e
 
t
e
x
t
 
f
o
r
 
`
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
|
"
p
i
x
e
l
"
|
"
c
o
m
p
u
t
e
"
)
]
`


 
 
(
`
S
h
a
d
e
r
U
t
i
l
i
t
y
.
R
e
g
e
x
F
u
n
c
t
i
o
n
`
,
 
`
H
l
s
l
F
u
n
c
t
i
o
n
I
n
f
o
.
c
s
`
)
.


-
 
*
*
S
o
u
r
c
e
-
l
e
v
e
l
 
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
 
d
e
p
e
n
d
s
 
o
n
*
*
:
 
t
e
x
t
u
r
e
/
s
a
m
p
l
e
r
 
p
a
i
r
i
n
g
 
b
y


 
 
t
h
e
 
`
n
a
m
e
#
#
S
a
m
p
l
e
r
`
 
s
u
f
f
i
x
;
 
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
 
d
e
t
e
c
t
i
o
n
 
b
y
 
n
a
m
e


 
 
(
`
M
a
r
k
D
e
p
t
h
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
a
m
p
l
e
r
s
`
)
;
 
d
e
p
t
h
-
t
e
x
t
u
r
e
 
d
e
t
e
c
t
i
o
n
 
b
y
 
r
e
g
e
x
i
n
g


 
 
`
D
E
F
I
N
E
_
T
E
X
2
D
_
D
E
P
T
H
*
`
 
m
a
c
r
o
 
c
a
l
l
s
 
+
 
S
P
I
R
-
V
 
b
i
n
a
r
y
 
p
a
t
c
h
i
n
g


 
 
(
`
S
p
i
r
v
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
P
a
t
c
h
e
r
`
,
 
b
e
c
a
u
s
e
 
d
x
c
 
e
m
i
t
s
 
`
O
p
T
y
p
e
I
m
a
g
e
 
D
e
p
t
h
=
u
n
k
n
o
w
n
`


 
 
w
h
i
c
h
 
n
a
g
a
 
r
e
j
e
c
t
s
)
;
 
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
 
c
o
m
p
a
n
i
o
n
s
 
r
e
c
o
g
n
i
z
e
d
 
v
i
a
 
d
x
c
'
s


 
 
i
m
p
l
i
c
i
t
 
`
c
o
u
n
t
e
r
.
v
a
r
.
<
n
a
m
e
>
`
 
n
a
m
i
n
g


 
 
(
`
S
h
a
d
e
r
R
e
f
l
e
c
t
i
o
n
I
n
f
o
.
I
s
C
o
u
n
t
e
r
C
o
m
p
a
n
i
o
n
`
)
.


-
 
*
*
C
a
c
h
i
n
g
 
/
 
h
o
t
 
r
e
l
o
a
d
*
*
:
 
`
S
h
a
d
e
r
C
a
c
h
e
`
 
(
`
I
S
h
a
d
e
r
C
a
c
h
e
`
)
 
s
t
o
r
e
s
 
o
n
e
 
f
i
l
e
 
p
e
r


 
 
(
s
h
a
d
e
r
,
 
d
e
f
i
n
e
s
)
 
k
e
y
e
d
 
b
y
 
t
h
e
 
X
x
H
a
s
h
6
4
 
o
f
 
t
h
e
 
*
f
l
a
t
t
e
n
e
d
*
 
s
o
u
r
c
e
 
t
e
x
t
;


 
 
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
 
i
s
 
r
e
-
d
e
r
i
v
e
d
 
f
r
o
m
 
t
h
e
 
c
a
c
h
e
d
 
S
P
I
R
-
V
 
o
n
 
l
o
a
d
.
 
H
o
t
 
r
e
l
o
a
d
 
g
o
e
s


 
 
t
h
r
o
u
g
h
 
`
A
s
s
e
t
H
o
t
R
e
l
o
a
d
e
r
S
h
a
d
e
r
H
L
S
L
`
 
→
 
r
e
-
f
l
a
t
t
e
n
 
→
 
`
S
h
a
d
e
r
.
U
n
s
a
f
e
H
o
t
R
e
l
o
a
d
(
t
e
x
t
)
`


 
 
(
d
e
f
a
u
l
t
 
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
n
l
y
,
 
c
l
e
a
r
s
 
a
l
l
 
c
a
c
h
e
s
,
 
b
u
m
p
s
 
`
_
v
e
r
s
i
o
n
`
)
.


-
 
*
*
P
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
*
*
:
 
p
l
a
i
n
 
p
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
 
t
h
r
e
a
d
e
d
 
f
r
o
m
 
`
M
a
t
e
r
i
a
l
.
S
e
t
D
e
f
i
n
e
s
`


 
 
t
o
 
d
x
c
 
`
-
D
`
;
 
p
e
r
-
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
 
m
o
d
u
l
e
s
 
a
n
d
 
p
i
p
e
l
i
n
e
s
 
c
a
c
h
e
d
 
b
y
 
d
e
f
i
n
e
s
-
s
t
r
i
n
g
 
h
a
s
h
.


-
 
*
*
A
s
s
e
t
s
*
*
:
 
`
.
h
l
s
l
`
 
f
i
l
e
s
 
a
r
e
 
a
s
s
e
t
s
 
(
`
A
s
s
e
t
L
o
a
d
e
r
S
h
a
d
e
r
H
L
S
L
`
,


 
 
`
A
s
s
e
t
L
o
a
d
e
r
S
h
a
d
e
r
H
L
S
L
I
n
c
l
u
d
e
`
)
;
 
s
h
a
d
e
r
s
 
s
h
i
p
 
a
s
 
s
o
u
r
c
e
 
a
n
d
 
c
o
m
p
i
l
e
 
a
t
 
r
u
n
t
i
m
e
.


 
 
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
.
g
e
n
.
c
s
`
/
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
P
a
t
h
.
g
e
n
.
c
s
`
 
h
o
l
d
 
p
a
t
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
.




I
n
v
e
n
t
o
r
y
:
 
7
2
 
`
.
h
l
s
l
`
 
+
 
1
9
 
`
.
h
l
s
l
i
`
 
i
n
 
s
o
u
r
c
e
 
d
i
r
s
.
 
`
C
o
r
e
.
h
l
s
l
i
`
 
i
s
 
i
n
c
l
u
d
e
d
 
b
y


6
1
 
o
f
 
7
2
 
`
.
h
l
s
l
`
 
f
i
l
e
s
;
 
i
n
c
l
u
d
e
 
d
e
p
t
h
 
≤
 
2
.
 
2
2
 
c
o
m
p
u
t
e
 
f
i
l
e
s
,
 
n
o
 
r
a
y
t
r
a
c
i
n
g
/
m
e
s
h
.


~
5
0
 
e
n
g
i
n
e
 
p
a
s
s
e
s
 
+
 
1
4
 
s
a
n
d
b
o
x
 
s
a
m
p
l
e
 
s
h
a
d
e
r
s
.
 
2
5
 
f
i
l
e
s
 
u
s
e
 
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
.


T
h
e
 
1
0
 
o
l
d
e
s
t
 
s
a
n
d
b
o
x
 
s
h
a
d
e
r
s
 
l
a
c
k
 
`
[
s
h
a
d
e
r
(
.
.
.
)
]
`
 
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
 
a
n
d
 
u
s
e
 
e
x
p
l
i
c
i
t


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
 
v
i
a
 
a
 
l
o
c
a
l
 
`
S
L
O
T
`
 
m
a
c
r
o
.




#
#
#
 
1
.
2
 
E
x
i
s
t
i
n
g
 
s
l
a
n
g
 
b
e
a
c
h
h
e
a
d
 
(
W
o
r
l
d
3
D
,
 
b
r
a
n
c
h
 
`
s
l
a
n
g
`
)




`
S
r
c
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
/
R
e
n
d
e
r
i
n
g
/
S
l
a
n
g
/
`
 
a
l
r
e
a
d
y
 
c
o
m
p
i
l
e
s
 
t
h
e
 
w
h
o
l
e
 
W
o
r
l
d
3
D
 
p
i
p
e
l
i
n
e


s
e
t
 
t
h
r
o
u
g
h
 
s
l
a
n
g
 
(
S
a
n
d
b
o
x
 
3
4
 
r
u
n
s
 
a
l
l
-
s
l
a
n
g
)
 
a
n
d
 
p
r
o
v
e
s
 
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
-
m
o
d
e
l


d
i
r
e
c
t
i
o
n
:
 
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
 
i
n
t
e
r
f
a
c
e
 
+
 
g
e
n
e
r
i
c
 
p
a
s
s
 
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


(
`
G
B
u
f
f
e
r
M
a
i
n
V
S
<
S
u
r
f
a
c
e
>
`
 
i
n
 
`
A
s
s
e
t
s
/
S
h
a
d
e
r
s
/
P
i
p
e
l
i
n
e
s
/
g
b
u
f
f
e
r
.
s
l
a
n
g
`
)


r
e
p
l
a
c
e
 
t
h
e
 
`
@
S
U
R
F
A
C
E
@
`
 
t
e
x
t
 
s
p
l
i
c
e
,
 
a
n
d
 
`
_
m
a
t
e
r
i
a
l
P
a
r
a
m
s
`
 
p
a
c
k
i
n
g
 
u
s
e
s


s
l
a
n
g
-
r
e
f
l
e
c
t
e
d
 
m
e
m
b
e
r
 
o
f
f
s
e
t
s
.




I
t
 
a
l
s
o
 
p
r
o
v
e
s
 
w
h
a
t
 
m
u
s
t
 
*
*
n
o
t
*
*
 
b
e
 
c
a
r
r
i
e
d
 
i
n
t
o
 
t
h
e
 
f
i
n
a
l
 
d
e
s
i
g
n
:




-
 
I
t
 
b
i
n
d
s
 
t
h
e
 
*
*
d
e
p
r
e
c
a
t
e
d
 
f
l
a
t
 
C
 
A
P
I
*
*
 
(
`
s
p
C
r
e
a
t
e
S
e
s
s
i
o
n
`
/
`
s
p
C
o
m
p
i
l
e
`
/


 
 
`
s
p
G
e
t
R
e
f
l
e
c
t
i
o
n
`
,
 
`
S
l
a
n
g
N
a
t
i
v
e
.
c
s
`
)
;
 
s
l
a
n
g
.
h
 
n
o
w
 
m
a
r
k
s
 
`
I
C
o
m
p
i
l
e
R
e
q
u
e
s
t
`


 
 
`
[
[
d
e
p
r
e
c
a
t
e
d
]
]
`
.


-
 
I
t
 
a
c
c
u
m
u
l
a
t
e
s
 
p
o
s
t
-
c
o
m
p
i
l
e
 
S
P
I
R
-
V
 
s
u
r
g
e
r
y
:
 
`
S
l
a
n
g
B
i
n
d
i
n
g
R
e
m
a
p
p
e
r
`
 
(
r
e
w
r
i
t
e
s


 
 
`
D
e
s
c
r
i
p
t
o
r
S
e
t
`
/
`
B
i
n
d
i
n
g
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
 
b
e
c
a
u
s
e
 
s
l
a
n
g
 
r
e
j
e
c
t
s
 
t
h
e
 
s
e
t
-
o
n
l
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
s
p
a
c
e
N
)
`
 
s
y
n
t
a
x
)
,
 
`
S
l
a
n
g
B
a
s
e
I
n
s
t
a
n
c
e
Z
e
r
o
e
r
`
 
(
w
g
p
u
 
r
e
j
e
c
t
s


 
 
`
g
l
_
B
a
s
e
I
n
s
t
a
n
c
e
`
)
,
 
r
e
d
u
n
d
a
n
t
-
`
D
r
a
w
P
a
r
a
m
e
t
e
r
s
`
-
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
t
r
i
p
p
i
n
g
,


 
 
`
-
e
m
i
t
-
s
p
i
r
v
-
v
i
a
-
g
l
s
l
`
 
f
o
r
 
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
x
c
e
p
t
 
o
n
e
 
s
h
a
d
e
r
 
w
h
o
s
e
 
g
l
s
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


 
 
n
a
g
a
 
r
e
j
e
c
t
s
,
 
a
n
d
 
`
S
l
a
n
g
S
p
i
r
v
F
a
c
t
s
`
 
(
r
e
-
r
e
a
d
s
 
t
h
r
e
a
d
 
g
r
o
u
p
 
s
i
z
e
 
/
 
s
t
o
r
a
g
e


 
 
f
o
r
m
a
t
s
 
f
r
o
m
 
S
P
I
R
-
V
 
b
e
c
a
u
s
e
 
t
h
e
 
f
l
a
t
 
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
 
p
a
t
h
 
d
o
e
s
n
'
t
 
e
x
p
o
s
e
 
t
h
e
m
)
.


-
 
I
t
 
r
o
u
t
e
s
 
a
r
o
u
n
d
 
t
h
e
 
e
n
g
i
n
e
 
f
a
c
i
l
i
t
i
e
s
 
(
p
r
o
v
i
d
e
r
-
m
o
d
e
 
`
S
h
a
d
e
r
`
 
c
t
o
r
)
 
i
n
s
t
e
a
d
 
o
f


 
 
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
m
;
 
e
n
g
i
n
e
 
b
u
i
l
t
-
i
n
s
 
a
n
d
 
t
h
e
 
g
l
a
s
s
 
m
a
t
e
r
i
a
l
 
p
a
s
s
 
r
e
m
a
i
n
 
d
x
c
-
o
n
l
y
.




#
#
#
 
1
.
3
 
W
h
y
 
m
i
g
r
a
t
e




d
x
c
 
r
e
p
l
a
c
e
m
e
n
t
 
i
s
 
t
h
e
 
l
e
a
s
t
 
o
f
 
i
t
.
 
T
h
e
 
g
o
a
l
s
 
a
r
e
:
 
m
o
d
u
l
e
s
/
i
m
p
o
r
t
 
i
n
s
t
e
a
d
 
o
f


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
 
f
l
a
t
t
e
n
i
n
g
;
 
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
 
+
 
g
e
n
e
r
i
c
s
 
i
n
s
t
e
a
d
 
o
f
 
m
a
c
r
o
 
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


a
n
d
 
t
e
x
t
 
s
p
l
i
c
i
n
g
;
 
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
<
T
>
`
 
i
n
s
t
e
a
d
 
o
f
 
t
h
e
 
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
 
m
a
c
r
o
 
l
a
y
e
r
;


f
i
r
s
t
-
c
l
a
s
s
 
s
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
 
i
n
s
t
e
a
d
 
o
f
 
a
 
h
a
n
d
-
m
a
i
n
t
a
i
n
e
d
 
S
P
I
R
-
V
 
p
a
r
s
e
r
 
p
l
u
s


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
e
s
;
 
a
n
d
 
a
 
s
i
n
g
l
e
-
s
o
u
r
c
e
 
p
a
t
h
 
t
o
 
f
u
t
u
r
e
 
t
a
r
g
e
t
s
 
(
W
G
S
L
 
f
o
r
 
a
 
w
e
b
/
D
a
w
n


b
u
i
l
d
)
 
v
i
a
 
t
h
e
 
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
y
s
t
e
m
.




#
#
 
2
.
 
G
o
a
l
s
 
a
n
d
 
n
o
n
-
g
o
a
l
s




*
*
G
o
a
l
s
*
*




1
.
 
A
l
l
 
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
 
a
n
d
 
s
a
n
d
b
o
x
 
s
h
a
d
e
r
s
 
c
o
m
p
i
l
e
 
a
s
 
n
a
t
i
v
e
 
s
l
a
n
g
 
m
o
d
u
l
e
s
.


2
.
 
T
h
e
 
c
o
m
p
i
l
e
/
r
u
n
t
i
m
e
/
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
 
s
t
a
c
k
 
i
s
 
r
e
d
e
s
i
g
n
e
d
 
a
r
o
u
n
d
 
t
h
e
 
m
o
d
e
r
n
 
s
l
a
n
g


 
 
 
A
P
I
 
(
`
I
G
l
o
b
a
l
S
e
s
s
i
o
n
`
/
`
I
S
e
s
s
i
o
n
`
/
`
I
M
o
d
u
l
e
`
/
`
I
C
o
m
p
o
n
e
n
t
T
y
p
e
`
)
;
 
d
x
c
,
 
d
x
c


 
 
 
b
i
n
d
i
n
g
s
,
 
`
I
n
c
l
u
d
e
H
e
l
p
e
r
`
,
 
a
n
d
 
t
h
e
 
c
u
s
t
o
m
 
S
P
I
R
-
V
 
r
e
f
l
e
c
t
o
r
 
a
r
e
 
r
e
m
o
v
e
d
.


3
.
 
T
h
e
 
s
h
a
d
e
r
-
f
a
c
i
n
g
 
b
i
n
d
i
n
g
 
c
o
n
t
r
a
c
t
 
s
t
a
y
s
 
n
a
m
e
-
b
a
s
e
d
 
a
n
d
 
k
e
e
p
s
 
t
h
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
-
g
r
o
u
p
e
d
 
s
e
t
 
l
a
y
o
u
t
 
f
r
o
m
 
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
B
i
n
d
G
r
o
u
p
R
e
f
a
c
t
o
r
P
l
a
n
.
m
d
`
.


4
.
 
E
v
e
r
y
 
p
h
a
s
e
 
i
s
 
i
n
d
e
p
e
n
d
e
n
t
l
y
 
g
r
e
e
n
:
 
`
V
a
l
i
d
a
t
e
S
h
a
d
e
r
`
,
 
u
n
i
t
 
t
e
s
t
s
,
 
a
n
d


 
 
 
s
c
r
e
e
n
s
h
o
t
-
d
i
f
f
 
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
 
a
g
a
i
n
s
t
 
p
r
e
-
c
h
a
n
g
e
 
c
a
p
t
u
r
e
s
.




*
*
N
o
n
-
g
o
a
l
s
*
*




-
 
N
o
 
n
e
w
 
G
P
U
 
b
a
c
k
e
n
d
 
(
w
g
p
u
 
r
e
m
a
i
n
s
 
t
h
e
 
o
n
l
y
 
o
n
e
)
;
 
m
u
l
t
i
-
t
a
r
g
e
t
 
o
u
t
p
u
t
 
i
s
 
k
e
p
t


 
 
p
o
s
s
i
b
l
e
,
 
n
o
t
 
b
u
i
l
t
.


-
 
N
o
 
b
i
n
d
l
e
s
s
 
r
e
w
r
i
t
e
 
o
f
 
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
y
s
t
e
m
 
(
s
l
a
n
g
 
`
D
e
s
c
r
i
p
t
o
r
H
a
n
d
l
e
<
T
>
`
 
i
s


 
 
n
o
t
e
d
 
a
s
 
a
 
f
u
t
u
r
e
 
d
i
r
e
c
t
i
o
n
 
o
n
l
y
)
.


-
 
N
o
 
d
y
n
a
m
i
c
 
d
i
s
p
a
t
c
h
 
(
`
d
y
n
`
)
 
i
n
 
m
a
t
e
r
i
a
l
s
;
 
s
t
a
t
i
c
 
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
 
o
n
l
y
.


-
 
N
o
 
v
i
s
u
a
l
/
b
e
h
a
v
i
o
r
a
l
 
c
h
a
n
g
e
s
 
t
o
 
r
e
n
d
e
r
i
n
g
 
o
u
t
p
u
t
.




#
#
 
3
.
 
K
e
y
 
d
e
s
i
g
n
 
d
e
c
i
s
i
o
n
s




#
#
#
 
D
1
 
—
 
A
 
d
e
d
i
c
a
t
e
d
 
S
h
a
d
e
r
S
y
s
t
e
m
 
o
w
n
s
 
m
o
d
u
l
e
s
;
 
t
h
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
 
i
s
 
d
e
m
o
t
e
d
 
t
o
 
f
i
l
e
 
p
r
o
v
i
d
e
r




s
l
a
n
g
 
`
i
m
p
o
r
t
`
 
i
s
 
a
 
c
o
m
p
i
l
e
r
-
d
o
m
a
i
n
 
c
o
n
c
e
p
t
:
 
m
o
d
u
l
e
s
 
a
r
e
 
r
e
s
o
l
v
e
d
 
b
y
 
n
a
m
e
 
a
g
a
i
n
s
t


s
e
s
s
i
o
n
 
s
e
a
r
c
h
 
p
a
t
h
s
,
 
c
a
c
h
e
d
 
p
e
r
 
s
e
s
s
i
o
n
,
 
c
o
m
p
i
l
e
d
 
s
e
p
a
r
a
t
e
l
y
 
t
o
 
I
R
,
 
a
n
d
 
t
h
e
i
r


d
e
p
e
n
d
e
n
c
y
 
g
r
a
p
h
 
i
s
 
q
u
e
r
y
a
b
l
e
 
(
`
I
M
o
d
u
l
e
:
:
g
e
t
D
e
p
e
n
d
e
n
c
y
F
i
l
e
C
o
u
n
t
/
P
a
t
h
`
)
.
 
T
h
e


p
e
r
-
f
i
l
e
 
a
s
s
e
t
-
l
o
a
d
e
r
 
m
o
d
e
l
 
(
"
o
n
e
 
`
.
h
l
s
l
`
 
→
 
f
l
a
t
t
e
n
 
→
 
o
n
e
 
`
S
h
a
d
e
r
`
"
)
 
h
a
s
 
n
o
 
p
l
a
c
e


f
o
r
 
a
 
m
o
d
u
l
e
 
g
r
a
p
h
,
 
f
o
r
 
`
.
s
l
a
n
g
-
m
o
d
u
l
e
`
 
b
i
n
a
r
y
 
a
r
t
i
f
a
c
t
s
,
 
o
r
 
f
o
r
 
r
e
v
e
r
s
e


i
n
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
 
(
a
 
c
h
a
n
g
e
d
 
l
i
b
 
m
u
s
t
 
i
n
v
a
l
i
d
a
t
e
 
i
t
s
 
i
m
p
o
r
t
e
r
s
,
 
n
o
t
 
i
t
s
e
l
f
)
.




T
h
e
r
e
f
o
r
e
:




-
 
*
*
S
h
a
d
e
r
S
y
s
t
e
m
*
*
 
(
n
e
w
,
 
§
4
.
2
)
 
o
w
n
s
 
t
h
e
 
s
l
a
n
g
 
g
l
o
b
a
l
 
s
e
s
s
i
o
n
,
 
s
e
s
s
i
o
n
s
 
p
e
r


 
 
s
e
a
r
c
h
-
p
a
t
h
 
s
e
t
,
 
t
h
e
 
m
o
d
u
l
e
 
c
a
c
h
e
,
 
t
h
e
 
`
.
s
l
a
n
g
-
m
o
d
u
l
e
`
 
d
i
s
k
 
c
a
c
h
e
,
 
d
e
p
e
n
d
e
n
c
y


 
 
t
r
a
c
k
i
n
g
,
 
d
i
a
g
n
o
s
t
i
c
s
,
 
a
n
d
 
h
o
t
-
r
e
l
o
a
d
 
i
n
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
.
 
C
a
l
l
e
r
s
 
a
s
k
 
f
o
r


 
 
`
G
e
t
S
h
a
d
e
r
(
m
o
d
u
l
e
N
a
m
e
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
`
 
—
 
n
o
t
 
`
L
o
a
d
<
S
h
a
d
e
r
>
(
p
a
t
h
)
`
.


-
 
T
h
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
 
k
e
e
p
s
 
e
x
a
c
t
l
y
 
t
w
o
 
r
o
l
e
s
:
 
(
a
)
 
b
a
c
k
i
n
g
 
t
h
e
 
s
l
a
n
g
 
v
i
r
t
u
a
l
 
f
i
l
e


 
 
s
y
s
t
e
m
 
(
`
I
S
l
a
n
g
F
i
l
e
S
y
s
t
e
m
E
x
t
`
,
 
e
v
o
l
u
t
i
o
n
 
o
f
 
`
S
l
a
n
g
F
i
l
e
S
y
s
t
e
m
.
c
s
`
)
 
s
o
 
p
a
k
 
f
i
l
e
s
,


 
 
e
m
b
e
d
d
e
d
 
a
s
s
e
t
s
 
a
n
d
 
t
h
e
 
d
i
r
e
c
t
o
r
y
 
w
a
t
c
h
e
r
 
k
e
e
p
 
w
o
r
k
i
n
g
 
—
 
s
l
a
n
g
 
i
m
p
o
r
t
s
 
a
r
e


 
 
f
u
l
l
y
 
v
i
r
t
u
a
l
i
z
a
b
l
e
;
 
(
b
)
 
f
i
l
e
-
c
h
a
n
g
e
 
n
o
t
i
f
i
c
a
t
i
o
n
s
 
t
h
a
t
 
f
e
e
d
 
S
h
a
d
e
r
S
y
s
t
e
m
'
s


 
 
r
e
v
e
r
s
e
-
d
e
p
e
n
d
e
n
c
y
 
i
n
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
.


-
 
`
A
s
s
e
t
L
o
a
d
e
r
S
h
a
d
e
r
H
L
S
L
`
,
 
`
A
s
s
e
t
L
o
a
d
e
r
S
h
a
d
e
r
H
L
S
L
I
n
c
l
u
d
e
`
 
a
n
d
 
`
I
n
c
l
u
d
e
H
e
l
p
e
r
`
 
a
r
e


 
 
d
e
l
e
t
e
d
 
a
t
 
t
e
a
r
d
o
w
n
.
 
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
*
.
g
e
n
.
c
s
`
 
s
w
i
t
c
h
e
s
 
f
r
o
m
 
s
h
a
d
e
r
 
p
a
t
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
 
t
o
 
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
 
c
o
n
s
t
a
n
t
s
.




#
#
#
 
D
2
 
—
 
B
i
n
d
i
n
g
 
m
o
d
e
l
:
 
e
x
p
l
i
c
i
t
 
s
e
t
 
i
n
d
e
x
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
 
b
i
n
d
i
n
g
 
w
i
t
h
i
n
 
s
e
t
,
 
n
a
m
e
-
b
a
s
e
d
 
r
e
s
o
l
u
t
i
o
n




K
e
e
p
 
t
h
e
 
c
u
r
r
e
n
t
 
p
h
i
l
o
s
o
p
h
y
 
—
 
s
l
a
n
g
'
s
 
s
e
m
a
n
t
i
c
s
 
a
r
e
 
c
o
m
p
a
t
i
b
l
e
 
a
n
d
 
s
t
r
o
n
g
e
r
:




-
 
s
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
 
b
i
n
d
i
n
g
s
 
*
*
b
e
f
o
r
e
 
d
e
a
d
-
c
o
d
e
 
e
l
i
m
i
n
a
t
i
o
n
*
*
,
 
s
o
 
u
n
u
s
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


 
 
k
e
e
p
 
t
h
e
i
r
 
s
l
o
t
s
 
a
n
d
 
l
a
y
o
u
t
s
 
a
r
e
 
s
t
a
b
l
e
 
a
c
r
o
s
s
 
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
.
 
T
h
i
s
 
s
u
b
s
u
m
e
s


 
 
t
h
e
 
`
-
f
s
p
v
-
p
r
e
s
e
r
v
e
-
b
i
n
d
i
n
g
s
`
 
b
e
h
a
v
i
o
r
 
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
l
i
e
s
 
o
n
,
 
w
i
t
h
 
n
o
 
f
l
a
g
.


-
 
S
e
t
s
 
a
r
e
 
e
x
p
l
i
c
i
t
 
a
n
d
 
f
o
l
l
o
w
 
t
h
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
M
a
t
e
r
i
a
l
B
i
n
d
G
r
o
u
p
R
e
f
a
c
t
o
r
P
l
a
n
.
m
d
`
 
§
3
.
1
)
:
 
0
 
=
 
f
r
a
m
e
,
 
1
 
=
 
p
a
s
s
,
 
2
 
=
 
m
a
t
e
r
i
a
l
,


 
 
3
 
=
 
d
r
a
w
.
 
I
n
 
s
l
a
n
g
 
s
o
u
r
c
e
s
 
a
 
s
e
t
 
i
s
 
e
x
p
r
e
s
s
e
d
 
b
y
 
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
<
T
>
`
 
p
l
a
c
e
m
e
n
t


 
 
/
 
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
,
 
s
p
a
c
e
N
)
`
 
/
 
`
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
,
 
s
)
]
`
 
a
s
 
a
p
p
r
o
p
r
i
a
t
e
;
 
w
i
t
h
i
n
 
a
 
s
e
t
,


 
 
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
 
—
 
n
e
v
e
r
 
w
r
i
t
t
e
n
 
b
y


 
 
h
a
n
d
,
 
n
e
v
e
r
 
r
e
a
d
 
b
y
 
C
#
.


-
 
C
#
 
c
o
n
t
i
n
u
e
s
 
t
o
 
r
e
s
o
l
v
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
 
b
y
 
n
a
m
e
 
t
h
r
o
u
g
h
 
`
S
h
a
d
e
r
R
e
f
l
e
c
t
i
o
n
I
n
f
o
`
;


 
 
`
V
a
l
i
d
a
t
e
B
i
n
d
G
r
o
u
p
L
a
y
o
u
t
s
`
 
(
g
r
o
u
p
 
c
o
u
n
t
 
≤
 
l
i
m
i
t
,
 
c
o
n
t
i
g
u
i
t
y
)
 
i
s
 
u
n
c
h
a
n
g
e
d
.


-
 
T
h
e
 
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
 
m
a
c
r
o
 
l
a
y
e
r
,
 
t
h
e
 
`
n
a
m
e
#
#
S
a
m
p
l
e
r
`
 
p
a
i
r
i
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
 
a
n
d
 
t
h
e


 
 
`
S
L
O
T
`
 
m
a
c
r
o
 
a
l
l
 
r
e
t
i
r
e
.
 
C
o
m
b
i
n
e
d
 
t
e
x
t
u
r
e
 
s
a
m
p
l
e
r
s
 
/
 
e
x
p
l
i
c
i
t


 
 
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
/
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
 
r
e
p
l
a
c
e
 
t
h
e
m
.




#
#
#
 
D
3
 
—
 
P
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
:
 
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
 
+
 
l
i
n
k
-
t
i
m
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
 
i
n
s
t
e
a
d
 
o
f
 
p
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




s
l
a
n
g
 
p
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
 
m
a
c
r
o
s
 
a
r
e
 
*
*
s
e
s
s
i
o
n
-
g
l
o
b
a
l
*
*
;
 
o
f
f
i
c
i
a
l
 
g
u
i
d
a
n
c
e
 
i
s
 
t
o
 
b
u
i
l
d


v
a
r
i
a
n
t
s
 
w
i
t
h
 
g
e
n
e
r
i
c
s
 
a
n
d
 
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
 
i
n
s
t
e
a
d
.
 
C
o
n
c
r
e
t
e
l
y
:




-
 
`
#
i
f
 
V
O
X
E
L
_
M
A
X
_
L
E
V
E
L
S
=
=
6
`
-
s
t
y
l
e
 
s
w
i
t
c
h
e
s
 
b
e
c
o
m
e
 
`
v
o
i
d
 
M
a
i
n
C
S
<
l
e
t
 
M
a
x
L
e
v
e
l
s
 
:
 
i
n
t
>
(
.
.
.
)
`


 
 
a
n
d
 
a
r
e
 
i
n
s
t
a
n
t
i
a
t
e
d
 
v
i
a
 
`
I
C
o
m
p
o
n
e
n
t
T
y
p
e
:
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
e
(
S
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
A
r
g
)
`
.


 
 
S
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
 
a
r
e
 
t
y
p
e
-
c
h
e
c
k
e
d
 
b
e
f
o
r
e
 
c
o
d
e
g
e
n
 
a
n
d
 
p
r
o
d
u
c
e
 
s
t
a
b
l
e
 
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
.


-
 
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
 
i
s
 
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
 
(
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
,
 
a
l
r
e
a
d
y
 
p
r
o
v
e
n


 
 
i
n
 
W
o
r
l
d
3
D
.


-
 
`
#
d
e
f
i
n
e
`
 
s
u
r
v
i
v
e
s
 
o
n
l
y
 
f
o
r
 
t
r
u
e
 
g
l
o
b
a
l
 
c
o
m
p
i
l
e
-
t
i
m
e
 
s
w
i
t
c
h
e
s
 
d
u
r
i
n
g
 
t
h
e


 
 
t
r
a
n
s
i
t
i
o
n
;
 
`
S
h
a
d
e
r
.
T
e
s
t
A
l
l
D
e
f
i
n
e
s
`
 
b
e
c
o
m
e
s
 
"
e
n
u
m
e
r
a
t
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
s
"
.


-
 
S
i
d
e
 
b
e
n
e
f
i
t
:
 
k
i
l
l
s
 
t
h
e
 
p
e
r
-
d
e
f
i
n
e
-
s
e
t
 
c
o
m
p
i
l
e
-
r
e
q
u
e
s
t
/
s
e
s
s
i
o
n
 
o
v
e
r
h
e
a
d
 
t
h
e


 
 
c
u
r
r
e
n
t
 
b
e
a
c
h
h
e
a
d
 
p
a
y
s
.




#
#
#
 
D
4
 
—
 
s
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
 
(
`
P
r
o
g
r
a
m
L
a
y
o
u
t
`
)
 
b
e
c
o
m
e
s
 
t
h
e
 
s
i
n
g
l
e
 
s
o
u
r
c
e
 
o
f
 
t
r
u
t
h




-
 
B
i
n
d
 
g
r
o
u
p
 
l
a
y
o
u
t
s
 
a
r
e
 
b
u
i
l
t
 
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
 
*
*
b
i
n
d
i
n
g
 
r
a
n
g
e
s
 
A
P
I
*
*


 
 
(
`
T
y
p
e
L
a
y
o
u
t
R
e
f
l
e
c
t
i
o
n
.
g
e
t
B
i
n
d
i
n
g
R
a
n
g
e
C
o
u
n
t
/
T
y
p
e
`
,


 
 
`
g
e
t
F
i
e
l
d
B
i
n
d
i
n
g
R
a
n
g
e
O
f
f
s
e
t
`
,
 
…
)
 
—
 
t
h
e
 
s
a
n
c
t
i
o
n
e
d
 
c
r
o
s
s
-
t
a
r
g
e
t
 
r
o
u
t
e
 
t
h
a
t
 
m
a
p
s


 
 
d
i
r
e
c
t
l
y
 
t
o
 
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
 
t
y
p
e
s
,
 
r
e
p
l
a
c
i
n
g
 
b
o
t
h
 
t
h
e
 
c
u
s
t
o
m
 
S
P
I
R
-
V
 
w
a
l
k
 
a
n
d
 
a
n
y


 
 
r
e
g
i
s
t
e
r
 
a
r
i
t
h
m
e
t
i
c
.


-
 
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
 
c
o
m
e
 
f
r
o
m
 
`
I
M
o
d
u
l
e
.
g
e
t
E
n
t
r
y
P
o
i
n
t
C
o
u
n
t
/
g
e
t
E
n
t
r
y
P
o
i
n
t
`
 
+


 
 
`
E
n
t
r
y
P
o
i
n
t
R
e
f
l
e
c
t
i
o
n
`
 
(
s
t
a
g
e
,
 
t
h
r
e
a
d
 
g
r
o
u
p
 
s
i
z
e
,
 
v
a
r
y
i
n
g
 
I
/
O
 
w
i
t
h
 
s
e
m
a
n
t
i
c
s
,


 
 
p
u
s
h
-
c
o
n
s
t
a
n
t
 
r
a
n
g
e
s
)
.
 
S
o
u
r
c
e
-
r
e
g
e
x
 
e
n
t
r
y
 
d
i
s
c
o
v
e
r
y
 
i
s
 
d
e
l
e
t
e
d
.


-
 
P
e
r
-
p
a
r
a
m
e
t
e
r
 
l
i
v
e
n
e
s
s
 
a
f
t
e
r
 
D
C
E
 
i
s
 
c
h
e
c
k
e
d
 
w
i
t
h


 
 
`
I
M
e
t
a
d
a
t
a
.
i
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
L
o
c
a
t
i
o
n
U
s
e
d
`
 
w
h
e
n
 
e
x
a
c
t
 
b
u
d
g
e
t
 
a
c
c
o
u
n
t
i
n
g
 
m
a
t
t
e
r
s
.


-
 
`
S
h
a
d
e
r
R
e
f
l
e
c
t
i
o
n
I
n
f
o
`
 
*
*
k
e
e
p
s
 
i
t
s
 
c
u
r
r
e
n
t
 
s
h
a
p
e
*
*
 
(
n
a
m
e
 
→
 
d
e
n
s
e
 
o
r
d
i
n
a
l
 
→


 
 
`
S
h
a
d
e
r
R
e
s
o
u
r
c
e
L
o
c
a
t
i
o
n
`
;
 
`
B
i
n
d
G
r
o
u
p
s
`
;
 
`
V
e
r
t
e
x
L
a
y
o
u
t
s
`
;
 
p
u
s
h
-
c
o
n
s
t
a
n
t
 
s
i
z
e
)
 
—


 
 
o
n
l
y
 
i
t
s
 
p
r
o
d
u
c
e
r
 
c
h
a
n
g
e
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
 
m
i
n
i
m
a
l
-
i
n
t
r
u
s
i
o
n
 
d
e
c
i
s
i
o
n
:


 
 
`
S
h
a
d
e
r
.
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
`
,
 
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
 
c
a
c
h
e
s
,
 
a
n
d
 
a
l
l
 
o
f


 
 
`
S
h
a
d
e
r
P
a
r
a
m
e
t
e
r
S
e
t
`
 
(
s
l
o
t
-
p
e
r
-
r
e
s
o
u
r
c
e
,
 
i
d
e
n
t
i
t
y
 
n
o
-
o
p
,
 
c
o
n
t
e
n
t
-
k
e
y
e
d
 
g
r
o
u
p


 
 
c
a
c
h
e
,
 
n
a
m
e
-
b
a
s
e
d
 
f
a
l
l
b
a
c
k
)
 
a
r
e
 
u
n
t
o
u
c
h
e
d
.


-
 
`
S
p
i
r
v
R
e
f
l
e
c
t
o
r
`
 
s
u
r
v
i
v
e
s
 
t
h
e
 
t
r
a
n
s
i
t
i
o
n
 
a
s
 
a
 
c
r
o
s
s
-
c
h
e
c
k
 
h
a
r
n
e
s
s
 
o
n
l
y
,
 
t
h
e
n


 
 
i
s
 
d
e
l
e
t
e
d
 
(
P
h
a
s
e
 
3
/
4
)
.




#
#
#
 
D
5
 
—
 
L
a
n
g
u
a
g
e
 
v
e
r
s
i
o
n
 
a
n
d
 
m
o
d
u
l
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
s




-
 
T
r
a
n
s
i
t
i
o
n
 
p
e
r
i
o
d
:
 
s
o
u
r
c
e
s
 
s
t
a
y
 
o
n
 
t
h
e
 
d
e
f
a
u
l
t
 
l
e
g
a
c
y
 
l
a
n
g
u
a
g
e
 
v
e
r
s
i
o
n


 
 
(
H
L
S
L
-
l
i
k
e
 
d
e
f
a
u
l
t
s
,
 
m
i
n
i
m
a
l
 
f
r
i
c
t
i
o
n
)
;
 
s
l
a
n
g
 
c
a
n
 
`
i
m
p
o
r
t
`
 
p
l
a
i
n
 
`
.
h
l
s
l
`
 
f
i
l
e
s


 
 
a
s
 
l
e
g
a
c
y
 
a
l
l
-
p
u
b
l
i
c
 
m
o
d
u
l
e
s
,
 
s
o
 
o
l
d
 
a
n
d
 
n
e
w
 
c
o
d
e
 
i
n
t
e
r
m
i
x
 
f
r
e
e
l
y
.


-
 
E
v
e
r
y
 
*
*
n
e
w
/
r
e
w
r
i
t
t
e
n
*
*
 
m
o
d
u
l
e
 
s
t
a
r
t
s
 
w
i
t
h
 
a
 
`
m
o
d
u
l
e
 
A
l
c
o
.
*
;
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
 
a
n
d


 
 
p
i
n
s
 
`
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
`
 
(
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
 
r
e
q
u
i
r
e
d
,
 
`
i
n
t
e
r
n
a
l
`
 
d
e
f
a
u
l
t


 
 
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
)
.
 
2
0
2
6
 
s
e
m
a
n
t
i
c
s
 
(
`
d
y
n
`
 
k
e
y
w
o
r
d
 
e
t
c
.
)
 
a
r
e
 
d
e
f
e
r
r
e
d
 
u
n
t
i
l
 
t
h
e


 
 
m
i
g
r
a
t
i
o
n
 
s
e
t
t
l
e
s
.


-
 
M
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
p
a
c
e
 
m
i
r
r
o
r
s
 
a
s
s
e
m
b
l
y
 
+
 
d
i
r
e
c
t
o
r
y
:


 
 
`
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
C
o
r
e
`
 
(
w
a
s
 
`
C
o
r
e
.
h
l
s
l
i
`
)
,
 
`
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
P
o
s
t
P
r
o
c
e
s
s
.
*
`
,


 
 
`
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
P
i
p
e
l
i
n
e
s
.
*
`
,
 
`
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
M
a
t
e
r
i
a
l
s
.
*
`
.


-
 
s
l
a
n
g
 
v
e
r
s
i
o
n
 
i
s
 
*
*
p
i
n
n
e
d
*
*
 
(
r
e
c
o
r
d
e
d
 
i
n
 
§
4
.
1
)
 
a
n
d
 
u
p
g
r
a
d
e
d
 
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


 
 
P
r
o
f
i
l
e
/
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
 
I
D
s
 
a
r
e
 
n
o
t
 
s
t
a
b
l
e
 
a
c
r
o
s
s
 
r
e
l
e
a
s
e
s
 
—
 
a
l
w
a
y
s
 
r
e
s
o
l
v
e
d
 
b
y


 
 
n
a
m
e
 
(
`
f
i
n
d
P
r
o
f
i
l
e
`
/
`
f
i
n
d
C
a
p
a
b
i
l
i
t
y
`
)
.




#
#
#
 
D
6
 
—
 
S
t
r
a
n
g
l
e
r
 
t
r
a
n
s
i
t
i
o
n
,
 
n
o
t
 
a
 
f
l
a
g
 
d
a
y




d
x
c
 
s
t
a
y
s
 
a
v
a
i
l
a
b
l
e
 
b
e
h
i
n
d
 
t
h
e
 
e
x
i
s
t
i
n
g
 
p
r
o
v
i
d
e
r
 
s
e
a
m
 
u
n
t
i
l
 
P
h
a
s
e
 
4
.
 
D
u
r
i
n
g


P
h
a
s
e
s
 
1
–
3
 
t
h
e
 
s
a
m
e
 
s
h
a
d
e
r
 
c
a
n
 
b
e
 
c
o
m
p
i
l
e
d
 
b
y
 
b
o
t
h
 
t
o
o
l
c
h
a
i
n
s
 
f
o
r
 
A
/
B


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
 
(
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
a
r
i
s
o
n
 
+
 
s
c
r
e
e
n
s
h
o
t
 
d
i
f
f
s
 
—
 
t
h
e
 
w
o
r
k
f
l
o
w
 
a
l
r
e
a
d
y
 
u
s
e
d


f
o
r
 
`
a
r
t
i
f
a
c
t
s
/
s
a
n
d
b
o
x
3
4
-
a
l
l
-
s
l
a
n
g
-
*
`
)
.
 
N
o
 
d
u
a
l
-
s
t
a
c
k
 
r
e
m
a
i
n
s
 
a
f
t
e
r
 
P
h
a
s
e
 
4
.




#
#
 
4
.
 
T
a
r
g
e
t
 
a
r
c
h
i
t
e
c
t
u
r
e




#
#
#
 
4
.
1
 
C
o
m
p
i
l
e
 
s
t
a
c
k
 
(
`
S
r
c
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
`
)




-
 
N
e
w
 
`
B
i
n
d
i
n
g
/
S
l
a
n
g
/
`
 
n
e
x
t
 
t
o
 
`
B
i
n
d
i
n
g
/
D
x
c
/
`
,
 
s
a
m
e
 
h
a
n
d
-
r
o
l
l
e
d
 
C
O
M
-
v
t
a
b
l
e
 
s
t
y
l
e


 
 
a
s
 
`
D
X
C
N
a
t
i
v
e
.
c
s
`
 
(
s
l
a
n
g
 
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
 
C
O
M
-
s
h
a
p
e
d
;
 
`
I
B
l
o
b
`
 
i
s


 
 
`
I
D
3
D
B
l
o
b
`
-
c
o
m
p
a
t
i
b
l
e
)
.
 
S
u
r
f
a
c
e
:
 
`
c
r
e
a
t
e
G
l
o
b
a
l
S
e
s
s
i
o
n
`


 
 
(
`
s
l
a
n
g
_
c
r
e
a
t
e
G
l
o
b
a
l
S
e
s
s
i
o
n
2
`
)
,
 
`
I
S
e
s
s
i
o
n
`
 
(
t
a
r
g
e
t
s
,
 
s
e
a
r
c
h
 
p
a
t
h
s
,
 
m
a
c
r
o
s
,


 
 
`
f
i
l
e
S
y
s
t
e
m
`
,
 
c
o
m
p
i
l
e
r
 
o
p
t
i
o
n
s
)
,
 
`
I
M
o
d
u
l
e
`
,
 
`
I
C
o
m
p
o
n
e
n
t
T
y
p
e
`
 
(
c
o
m
p
o
s
i
t
e
,
 
l
i
n
k
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
e
,
 
`
g
e
t
L
a
y
o
u
t
`
,
 
`
g
e
t
E
n
t
r
y
P
o
i
n
t
C
o
d
e
`
)
,
 
`
I
S
l
a
n
g
F
i
l
e
S
y
s
t
e
m
E
x
t
`
.


-
 
O
n
e
 
s
h
a
r
e
d
 
`
I
G
l
o
b
a
l
S
e
s
s
i
o
n
`
;
 
p
e
r
-
(
s
e
a
r
c
h
-
p
a
t
h
-
s
e
t
)
 
`
I
S
e
s
s
i
o
n
`
.
 
C
o
n
s
i
d
e
r


 
 
`
s
l
a
n
g
_
c
r
e
a
t
e
G
l
o
b
a
l
S
e
s
s
i
o
n
W
i
t
h
o
u
t
C
o
r
e
M
o
d
u
l
e
`
 
+
 
e
m
b
e
d
d
e
d
 
c
o
r
e
 
m
o
d
u
l
e
 
f
o
r
 
s
t
a
r
t
u
p


 
 
c
o
s
t
 
i
f
 
p
r
o
f
i
l
i
n
g
 
s
h
o
w
s
 
i
t
 
m
a
t
t
e
r
s
.


-
 
D
i
a
g
n
o
s
t
i
c
s
:
 
e
v
e
r
y
 
c
a
l
l
 
c
o
l
l
e
c
t
s
 
i
t
s
 
`
I
B
l
o
b
*
*
`
 
d
i
a
g
n
o
s
t
i
c
s
 
b
l
o
b
 
(
n
o
n
-
n
u
l
l
 
e
v
e
n


 
 
o
n
 
s
u
c
c
e
s
s
 
—
 
c
a
r
r
i
e
s
 
w
a
r
n
i
n
g
s
)
,
 
s
u
r
f
a
c
e
d
 
w
i
t
h
 
f
i
l
e
/
l
i
n
e
 
i
n
t
o
 
t
h
e
 
e
n
g
i
n
e
'
s


 
 
s
h
a
d
e
r
 
e
r
r
o
r
 
r
e
p
o
r
t
i
n
g
.
 
R
e
p
l
a
c
e
s
 
e
r
r
o
r
-
s
t
r
i
n
g
 
s
c
r
a
p
i
n
g
.


-
 
s
l
a
n
g
 
n
a
t
i
v
e
 
b
i
n
a
r
i
e
s
 
m
o
v
e
 
f
r
o
m
 
`
S
r
c
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
/
r
u
n
t
i
m
e
s
/
`
 
t
o


 
 
`
S
r
c
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
/
r
u
n
t
i
m
e
s
/
<
r
i
d
>
/
n
a
t
i
v
e
/
`
,
 
s
h
i
p
p
e
d
 
b
y


 
 
`
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
c
s
p
r
o
j
`
 
e
x
a
c
t
l
y
 
l
i
k
e
 
`
d
x
c
o
m
p
i
l
e
r
.
d
l
l
`
 
t
o
d
a
y
.
 
T
h
e
 
p
i
n
n
e
d


 
 
s
l
a
n
g
 
v
e
r
s
i
o
n
 
i
s
 
r
e
c
o
r
d
e
d
 
a
t
 
t
h
e
 
t
o
p
 
o
f
 
`
B
i
n
d
i
n
g
/
S
l
a
n
g
/
`
.


-
 
M
a
n
a
g
e
d
 
f
a
c
a
d
e
:
 
`
S
l
a
n
g
C
o
m
p
i
l
e
r
`
 
w
i
t
h
 
o
p
e
r
a
t
i
o
n
s


 
 
`
L
o
a
d
M
o
d
u
l
e
(
n
a
m
e
)
`
,
 
`
C
o
m
p
i
l
e
(
m
o
d
u
l
e
,
 
e
n
t
r
i
e
s
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
,
 
t
a
r
g
e
t
)
`
 
→


 
 
p
e
r
-
e
n
t
r
y
 
S
P
I
R
-
V
 
+
 
`
P
r
o
g
r
a
m
L
a
y
o
u
t
`
.
 
N
o
 
S
P
I
R
-
V
 
p
o
s
t
-
p
r
o
c
e
s
s
i
n
g
 
h
o
o
k
s
 
i
n
 
t
h
e


 
 
n
e
w
 
A
P
I
 
(
s
e
e
 
P
h
a
s
e
 
3
 
f
o
r
 
r
e
t
i
r
i
n
g
 
t
h
e
 
e
x
i
s
t
i
n
g
 
o
n
e
s
)
.




#
#
#
 
4
.
2
 
S
h
a
d
e
r
S
y
s
t
e
m
 
(
n
e
w
 
r
u
n
t
i
m
e
 
s
e
r
v
i
c
e
,
 
`
S
r
c
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
/
S
h
a
d
e
r
/
`
)




R
e
s
p
o
n
s
i
b
i
l
i
t
i
e
s
:




-
 
M
o
d
u
l
e
 
c
a
c
h
e
 
k
e
y
e
d
 
b
y
 
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
;
 
d
e
p
e
n
d
e
n
c
y
 
g
r
a
p
h
 
f
r
o
m


 
 
`
I
M
o
d
u
l
e
.
g
e
t
D
e
p
e
n
d
e
n
c
y
F
i
l
e
P
a
t
h
`
;
 
r
e
v
e
r
s
e
-
d
e
p
e
n
d
e
n
c
y
 
i
n
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
 
o
n
 
f
i
l
e
-
c
h
a
n
g
e


 
 
n
o
t
i
f
i
c
a
t
i
o
n
s
 
(
r
e
p
l
a
c
e
s
 
`
A
s
s
e
t
H
o
t
R
e
l
o
a
d
e
r
S
h
a
d
e
r
H
L
S
L
`
 
+
 
`
U
n
s
a
f
e
H
o
t
R
e
l
o
a
d
(
t
e
x
t
)
`
;


 
 
a
 
l
i
b
 
e
d
i
t
 
n
o
w
 
i
n
v
a
l
i
d
a
t
e
s
 
e
x
a
c
t
l
y
 
i
t
s
 
i
m
p
o
r
t
e
r
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
 
c
o
m
p
i
l
e
d


 
 
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
 
o
f
 
t
h
e
m
 
—
 
n
o
t
 
j
u
s
t
 
t
h
e
 
d
e
f
a
u
l
t
 
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
)
.


-
 
D
i
s
k
 
c
a
c
h
e
 
r
e
p
l
a
c
i
n
g
 
`
S
h
a
d
e
r
C
a
c
h
e
`
:
 
t
w
o
 
l
a
y
e
r
s
.
 
(
a
)
 
`
.
s
l
a
n
g
-
m
o
d
u
l
e
`
 
I
R
 
b
l
o
b
s


 
 
(
`
I
M
o
d
u
l
e
.
s
e
r
i
a
l
i
z
e
`
 
/
 
`
I
S
e
s
s
i
o
n
.
l
o
a
d
M
o
d
u
l
e
F
r
o
m
I
R
B
l
o
b
`
,


 
 
`
i
s
B
i
n
a
r
y
M
o
d
u
l
e
U
p
T
o
D
a
t
e
`
)
;
 
(
b
)
 
l
i
n
k
e
d
-
p
r
o
g
r
a
m
 
c
a
c
h
e
 
k
e
y
e
d
 
b
y


 
 
(
m
o
d
u
l
e
 
I
R
 
h
a
s
h
,
 
e
n
t
r
y
 
s
e
t
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
 
a
r
g
s
,
 
t
a
r
g
e
t
,
 
s
l
a
n
g
 
v
e
r
s
i
o
n
)
.


 
 
*
*
C
a
v
e
a
t
 
f
r
o
m
 
t
h
e
 
s
l
a
n
g
 
d
o
c
s
*
*
:
 
a
 
b
i
n
a
r
y
 
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
 
p
r
i
m
a
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
 
s
o
u
r
c
e


 
 
i
s
 
a
b
s
e
n
t
 
f
r
o
m
 
t
h
e
 
s
e
a
r
c
h
 
p
a
t
h
s
 
i
s
 
a
c
c
e
p
t
e
d
 
a
s
 
u
p
-
t
o
-
d
a
t
e
 
w
i
t
h
o
u
t
 
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
 
—


 
 
s
h
i
p
p
e
d
 
b
u
i
l
d
s
 
m
u
s
t
 
e
i
t
h
e
r
 
i
n
c
l
u
d
e
 
s
o
u
r
c
e
s
 
o
r
 
e
m
b
e
d
 
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
 
v
e
r
s
i
o
n
 
s
t
a
m
p


 
 
i
n
 
t
h
e
 
c
a
c
h
e
 
k
e
y
.


-
 
`
G
e
t
S
h
a
d
e
r
(
m
o
d
u
l
e
N
a
m
e
)
`
 
/
 
`
G
e
t
S
h
a
d
e
r
(
m
o
d
u
l
e
N
a
m
e
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
.
.
.
)
`
 
r
e
t
u
r
n
i
n
g


 
 
t
h
e
 
u
n
i
f
i
e
d
 
`
S
h
a
d
e
r
`
 
(
§
4
.
4
)
.
 
B
u
i
l
t
-
i
n
 
s
h
a
d
e
r
s
 
r
e
g
i
s
t
e
r
 
b
y
 
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


 
 
(
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
P
a
t
h
.
g
e
n
.
c
s
`
 
r
e
g
e
n
e
r
a
t
i
o
n
)
.


-
 
H
o
t
 
r
e
l
o
a
d
:
 
w
a
t
c
h
e
r
 
e
v
e
n
t
 
→
 
m
a
p
 
f
i
l
e
 
p
a
t
h
 
→
 
m
o
d
u
l
e
(
s
)
 
→
 
i
n
v
a
l
i
d
a
t
e
 
→


 
 
`
S
h
a
d
e
r
`
 
`
_
v
e
r
s
i
o
n
`
 
b
u
m
p
 
→
 
l
a
z
y
 
p
i
p
e
l
i
n
e
 
r
e
b
u
i
l
d
 
v
i
a
 
t
h
e
 
e
x
i
s
t
i
n
g


 
 
`
T
r
y
U
p
d
a
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
C
o
n
t
e
x
t
`
 
m
e
c
h
a
n
i
s
m
.




#
#
#
 
4
.
3
 
S
h
a
d
e
r
 
s
o
u
r
c
e
 
o
r
g
a
n
i
z
a
t
i
o
n




-
 
O
n
e
 
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
 
p
e
r
 
m
o
d
u
l
e
,
 
`
m
o
d
u
l
e
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
 
f
i
r
s
t
 
l
i
n
e
,
 
`
p
u
b
l
i
c
`
 
o
n
l
y
 
a
t


 
 
A
P
I
 
b
o
u
n
d
a
r
i
e
s
.
 
S
h
a
r
e
d
 
l
i
b
s
 
u
n
d
e
r
 
`
L
i
b
s
/
`
 
b
e
c
o
m
e
 
r
e
a
l
 
m
o
d
u
l
e
s


 
 
(
`
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
C
o
r
e
`
,
 
`
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
R
e
v
e
r
s
e
d
D
e
p
t
h
`
,
 
`
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
V
o
x
e
l
C
o
m
m
o
n
`
,


 
 
…
)
 
—
 
i
m
p
o
r
t
 
g
r
a
p
h
 
r
e
p
l
a
c
e
s
 
t
h
e
 
i
n
c
l
u
d
e
 
g
r
a
p
h
;
 
n
o
 
i
n
c
l
u
d
e
 
g
u
a
r
d
s
,
 
n
o


 
 
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
-
o
r
d
e
r
 
c
o
u
p
l
i
n
g
.


-
 
B
i
n
d
i
n
g
 
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
 
u
s
e
 
p
l
a
i
n
 
s
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
 
i
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
<
T
>
`


 
 
g
r
o
u
p
i
n
g
s
 
p
e
r
 
f
r
e
q
u
e
n
c
y
 
s
e
t
 
(
D
2
)
.
 
T
h
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
 
c
o
n
s
t
a
n
t
s
 
l
i
v
e
 
i
n


 
 
`
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
C
o
r
e
`
.


-
 
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
 
k
e
e
p
 
`
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
|
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
|
"
c
o
m
p
u
t
e
"
)
]
`
 
+


 
 
`
M
a
i
n
V
S
/
M
a
i
n
P
S
/
M
a
i
n
C
S
`
 
n
a
m
i
n
g
;
 
t
h
e
 
1
0
 
l
e
g
a
c
y
 
s
a
n
d
b
o
x
 
s
h
a
d
e
r
s
 
g
a
i
n
 
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
.


-
 
D
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
s
 
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
 
s
a
m
p
l
e
r
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
 
w
i
t
h
 
t
h
e
i
r
 
r
e
a
l
 
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
 
(
n
o
 
m
a
c
r
o
 
m
a
r
k
e
r
,
 
n
o
 
n
a
m
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
)
.
 
I
f
 
a
n
y
 
n
a
g
a
-
f
a
c
i
n
g
 
S
P
I
R
-
V
 
g
a
p


 
 
r
e
m
a
i
n
s
 
(
P
h
a
s
e
 
3
 
v
e
r
i
f
i
e
s
)
,
 
i
t
 
i
s
 
a
n
n
o
t
a
t
e
d
 
w
i
t
h
 
a
 
u
s
e
r
-
d
e
f
i
n
e
d
 
a
t
t
r
i
b
u
t
e


 
 
(
`
[
A
l
c
o
D
e
p
t
h
]
`
,
 
r
e
f
l
e
c
t
a
b
l
e
 
v
i
a
 
`
f
i
n
d
U
s
e
r
A
t
t
r
i
b
u
t
e
B
y
N
a
m
e
`
)
 
—
 
n
e
v
e
r
 
w
i
t
h


 
 
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
e
s
.


-
 
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
s
:
 
d
e
c
l
a
r
e
 
c
o
u
n
t
e
r
 
b
u
f
f
e
r
s
 
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
 
w
h
e
r
e
 
n
e
e
d
e
d
,


 
 
o
r
 
r
e
l
y
 
o
n
 
s
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
'
s
 
e
x
p
l
i
c
i
t
 
r
e
p
r
e
s
e
n
t
a
t
i
o
n
 
—
 
e
i
t
h
e
r
 
w
a
y
 
t
h
e


 
 
`
c
o
u
n
t
e
r
.
v
a
r
.
<
n
a
m
e
>
`
 
n
a
m
e
-
p
a
i
r
i
n
g
 
l
o
g
i
c
 
d
i
e
s
 
(
P
h
a
s
e
 
3
)
.




#
#
#
 
4
.
4
 
R
u
n
t
i
m
e
 
`
S
h
a
d
e
r
`
 
/
 
p
i
p
e
l
i
n
e
 
l
a
y
e
r




-
 
T
h
e
 
t
w
o
 
`
S
h
a
d
e
r
`
 
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
 
m
o
d
e
s
 
(
t
e
x
t
-
m
o
d
e
 
d
x
c
 
p
i
p
e
l
i
n
e
 
v
s
 
p
r
o
v
i
d
e
r


 
 
c
a
l
l
b
a
c
k
)
 
u
n
i
f
y
 
i
n
t
o
 
o
n
e
:
 
a
 
`
S
h
a
d
e
r
`
 
i
s
 
*
*
(
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
,
 
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
*
*
 
p
r
o
d
u
c
e
d
 
b
y
 
S
h
a
d
e
r
S
y
s
t
e
m
.
 
`
R
e
n
d
e
r
i
n
g
S
y
s
t
e
m
.
C
r
e
a
t
e
S
h
a
d
e
r
(
t
e
x
t
)
`


 
 
a
n
d
 
t
h
e
 
p
r
o
v
i
d
e
r
 
c
t
o
r
 
a
r
e
 
b
o
t
h
 
r
e
m
o
v
e
d
 
a
t
 
t
e
a
r
d
o
w
n
.


-
 
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
`
 
/
 
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
`
 
s
i
g
n
a
t
u
r
e
s
 
a
n
d
 
c
a
c
h
e
-
k
e
y


 
 
s
t
r
u
c
t
u
r
e
 
a
r
e
 
u
n
c
h
a
n
g
e
d
;
 
"
d
e
f
i
n
e
s
"
 
i
n
 
k
e
y
s
 
b
e
c
o
m
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
 
i
d
e
n
t
i
t
y
.


-
 
`
P
r
e
c
o
m
p
i
l
e
`
 
a
n
d
 
`
T
e
s
t
A
l
l
D
e
f
i
n
e
s
`
 
b
e
c
o
m
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
 
e
n
u
m
e
r
a
t
i
o
n


 
 
(
`
T
e
s
t
A
l
l
S
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
`
)
,
 
s
t
i
l
l
 
d
r
i
v
e
n
 
f
r
o
m
 
t
h
e
 
m
o
d
u
l
e
 
s
o
u
r
c
e
.




#
#
#
 
4
.
5
 
M
a
t
e
r
i
a
l
 
s
y
s
t
e
m




>
 
*
*
S
t
a
t
u
s
:
 
d
o
n
e
 
(
s
l
a
n
g
 
b
r
a
n
c
h
)
,
 
p
r
o
m
o
t
e
d
 
t
o
 
e
n
g
i
n
e
 
i
n
f
r
a
s
t
r
u
c
t
u
r
e
.
*
*
 
T
h
e
 
t
e
x
t


>
 
s
p
l
i
c
e
 
a
n
d
 
r
e
g
e
x
 
p
a
c
k
i
n
g
 
a
r
e
 
g
o
n
e
.
 
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
 
(
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
)


>
 
c
o
m
p
o
s
e
s
 
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
 
w
i
t
h
 
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
s
 
v
i
a
 
g
e
n
e
r
i
c
 
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


>
 
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
;
 
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
 
a
n
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
`


>
 
(
b
o
t
h
 
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
)
 
f
o
r
m
 
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
-
a
g
n
o
s
t
i
c
 
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
y
 
—
 
t
h
e
r
e
 
i
s


>
 
n
o
 
p
a
s
s
 
a
b
s
t
r
a
c
t
i
o
n
:
 
r
e
n
d
e
r
e
r
s
 
o
w
n
 
t
h
e
i
r
 
t
e
m
p
l
a
t
e
 
l
i
b
r
a
r
y
 
a
n
d
 
m
a
t
e
r
i
a
l


>
 
f
a
c
t
o
r
y
 
a
n
d
 
c
a
l
l
 
`
C
o
m
p
i
l
e
(
a
s
s
e
t
,
 
t
e
m
p
l
a
t
e
,
 
s
p
e
c
A
r
g
s
,
 
f
a
c
t
o
r
y
)
`
 
d
i
r
e
c
t
l
y
,


>
 
c
a
c
h
i
n
g
 
p
e
r
-
a
s
s
e
t
 
m
a
t
e
r
i
a
l
s
 
i
n
 
`
C
o
n
d
i
t
i
o
n
a
l
W
e
a
k
T
a
b
l
e
`
s
.
 
A
s
s
e
t
s
 
a
r
e


>
 
p
o
l
y
m
o
r
p
h
i
c
 
(
a
 
`
.
a
m
a
t
`
 
f
i
l
e
'
s
 
`
t
y
p
e
`
 
d
i
s
c
r
i
m
i
n
a
t
o
r
 
s
e
l
e
c
t
s
 
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


>
 
f
a
m
i
l
y
'
s
 
s
c
h
e
m
a
;
 
t
h
e
 
l
o
a
d
e
r
 
l
i
v
e
s
 
i
n
 
A
l
c
o
.
E
n
g
i
n
e
)
.
 
W
o
r
l
d
3
D
 
c
o
n
t
r
i
b
u
t
e
s
 
t
h
e


>
 
`
P
b
r
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
`
 
f
a
m
i
l
y
 
a
n
d
 
i
t
s
 
m
a
t
e
r
i
a
l
-
c
a
r
r
y
i
n
g
 
r
e
n
d
e
r
e
r
s
.


>
 
O
n
e
 
d
e
v
i
a
t
i
o
n
 
f
r
o
m
 
t
h
e
 
s
k
e
t
c
h
 
b
e
l
o
w
:
 
s
u
r
f
a
c
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
 
u
s
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


>
 
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
 
i
n
 
s
p
a
c
e
2
 
(
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
w
i
d
e
 
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
)
,
 
n
o
t


>
 
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
<
T
>
`
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
.




`
S
h
a
d
e
r
P
a
r
a
m
e
t
e
r
S
e
t
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
`
,
 
`
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
`
,
 
`
C
o
m
p
u
t
e
M
a
t
e
r
i
a
l
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
I
n
s
t
a
n
c
e
`
 
a
r
e
 
*
*
u
n
c
h
a
n
g
e
d
*
*
 
—
 
t
h
e
y
 
c
o
n
s
u
m
e
 
`
S
h
a
d
e
r
R
e
f
l
e
c
t
i
o
n
I
n
f
o
`
,
 
w
h
o
s
e


s
h
a
p
e
 
i
s
 
p
r
e
s
e
r
v
e
d
 
(
D
4
)
.
 
C
h
a
n
g
e
s
 
a
r
e
 
c
o
n
f
i
n
e
d
 
t
o
 
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
:




-
 
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
:
 
t
h
e
 
`
@
S
U
R
F
A
C
E
@
`
 
t
e
x
t
 
s
p
l
i
c
e
 
a
n
d
 
t
h
e
 
H
L
S
L
-
s
u
r
f
a
c
e
 
f
l
o
a
t
4


 
 
r
e
g
e
x
 
p
a
c
k
i
n
g
 
a
r
e
 
d
e
l
e
t
e
d
.
 
E
v
e
r
y
 
m
a
t
e
r
i
a
l
 
s
u
r
f
a
c
e
 
i
s
 
a
n
 
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
;
 
p
e
r
-
(
a
s
s
e
t
,
 
p
a
s
s
)
 
s
h
a
d
e
r
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
 
i
n
s
t
a
n
t
i
a
t
i
o
n
s
;


 
 
p
a
r
a
m
e
t
e
r
-
b
l
o
c
k
 
p
a
c
k
i
n
g
 
c
o
n
t
i
n
u
e
s
 
v
i
a
 
s
l
a
n
g
-
r
e
f
l
e
c
t
e
d
 
m
e
m
b
e
r
 
o
f
f
s
e
t
s


 
 
(
`
[
M
a
t
e
r
i
a
l
P
a
r
a
m
s
]
`
-
m
a
r
k
e
d
 
b
l
o
c
k
s
,
 
d
i
s
c
o
v
e
r
e
d
 
b
y
 
m
a
r
k
e
r
 
n
o
t
 
n
a
m
e
)
 
a
n
d
 
i
s


 
 
p
r
o
m
o
t
e
d
 
o
u
t
 
o
f
 
W
o
r
l
d
3
D
 
i
n
t
o
 
t
h
e
 
s
h
a
r
e
d
 
p
a
t
h
.


-
 
M
a
t
e
r
i
a
l
 
p
a
r
a
m
e
t
e
r
 
b
l
o
c
k
 
b
e
c
o
m
e
s
 
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
<
M
a
t
e
r
i
a
l
P
a
r
a
m
s
>
`
 
i
n
 
s
e
t
 
2
,


 
 
m
a
t
c
h
i
n
g
 
t
h
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
-
g
r
o
u
p
 
d
e
s
i
g
n
.


-
 
T
h
e
 
g
l
a
s
s
 
p
a
s
s
 
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
i
l
e
r
.
c
s
`
 
"
H
L
S
L
-
o
n
l
y
 
f
o
r
 
n
o
w
"
)
 
g
e
t
s
 
a
 
s
l
a
n
g


 
 
t
e
m
p
l
a
t
e
 
l
i
k
e
 
t
h
e
 
o
t
h
e
r
 
p
a
s
s
e
s
.




#
#
 
5
.
 
S
l
a
n
g
 
f
e
a
t
u
r
e
 
a
d
o
p
t
i
o
n
 
m
a
p




|
 
C
u
r
r
e
n
t
 
p
a
t
t
e
r
n
 
|
 
R
e
p
l
a
c
e
d
 
b
y
 
|


|
-
-
-
|
-
-
-
|


|
 
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
 
/
 
`
.
h
l
s
l
i
`
,
 
`
I
n
c
l
u
d
e
H
e
l
p
e
r
`
 
f
l
a
t
t
e
n
i
n
g
 
|
 
`
m
o
d
u
l
e
`
 
+
 
`
i
m
p
o
r
t
`
,
 
`
n
a
m
e
s
p
a
c
e
 
A
l
c
o
.
*
`
 
|


|
 
`
D
E
F
I
N
E
_
U
N
I
F
O
R
M
/
T
E
X
2
D
_
S
A
M
P
L
E
/
S
T
O
R
A
G
E
/
.
.
.
`
 
m
a
c
r
o
s
 
|
 
p
l
a
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
s
 
g
r
o
u
p
e
d
 
i
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
<
T
>
`
 
p
e
r
 
f
r
e
q
u
e
n
c
y
 
s
e
t
 
|


|
 
`
n
a
m
e
#
#
S
a
m
p
l
e
r
`
 
p
a
i
r
i
n
g
,
 
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
 
n
a
m
e
 
d
e
t
e
c
t
i
o
n
 
|
 
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
 
/
 
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
;
 
r
e
f
l
e
c
t
e
d
 
k
i
n
d
s
 
|


|
 
`
#
i
f
`
 
/
 
d
e
f
i
n
e
 
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
,
 
`
T
e
s
t
A
l
l
D
e
f
i
n
e
s
`
 
|
 
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
s
 
(
`
<
l
e
t
 
N
 
:
 
i
n
t
>
`
)
 
+
 
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
`
;
 
`
s
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
`
 
|


|
 
`
@
S
U
R
F
A
C
E
@
`
 
t
e
x
t
 
s
p
l
i
c
e
,
 
s
u
r
f
a
c
e
-
b
y
-
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
 
|
 
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
 
i
n
t
e
r
f
a
c
e
 
+
 
g
e
n
e
r
i
c
 
e
n
t
r
y
 
i
n
s
t
a
n
t
i
a
t
i
o
n
 
(
a
l
r
e
a
d
y
 
p
r
o
v
e
n
)
 
|


|
 
M
a
c
r
o
 
c
o
n
s
t
a
n
t
s
 
(
`
V
O
X
E
L
_
*
`
,
 
c
l
o
u
d
/
a
t
m
o
s
p
h
e
r
e
)
 
|
 
`
s
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
`
 
/
 
c
o
n
s
t
e
x
p
r
 
|


|
 
`
_
_
S
L
A
N
G
_
_
`
 
/
 
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
 
`
#
i
f
d
e
f
`
 
g
u
a
r
d
s
 
|
 
`
[
r
e
q
u
i
r
e
(
.
.
.
)
]
`
 
c
a
p
a
b
i
l
i
t
i
e
s
,
 
`
_
_
t
a
r
g
e
t
_
s
w
i
t
c
h
`
 
w
h
e
r
e
 
u
n
a
v
o
i
d
a
b
l
e
 
|


|
 
R
e
g
e
x
 
d
e
p
t
h
-
t
e
x
t
u
r
e
 
m
a
r
k
e
r
s
 
|
 
r
e
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
 
t
y
p
e
s
;
 
`
[
A
l
c
o
D
e
p
t
h
]
`
 
u
s
e
r
 
a
t
t
r
i
b
u
t
e
 
a
s
 
f
a
l
l
b
a
c
k
 
m
a
r
k
e
r
 
|


|
 
`
c
o
u
n
t
e
r
.
v
a
r
.
<
n
a
m
e
>
`
 
p
a
i
r
i
n
g
 
|
 
e
x
p
l
i
c
i
t
 
c
o
u
n
t
e
r
 
b
u
f
f
e
r
s
 
o
r
 
s
l
a
n
g
-
r
e
f
l
e
c
t
e
d
 
c
o
u
n
t
e
r
s
,
 
p
a
i
r
e
d
 
b
y
 
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
 
|


|
 
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
s
p
a
c
e
N
)
`
 
a
u
t
o
-
b
i
n
d
i
n
g
 
|
 
e
x
p
l
i
c
i
t
 
s
e
t
s
 
v
i
a
 
p
a
r
a
m
e
t
e
r
 
b
l
o
c
k
s
;
 
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
 
b
i
n
d
i
n
g
s
 
w
i
t
h
i
n
 
s
e
t
 
|


|
 
P
e
r
-
t
y
p
e
 
h
e
l
p
e
r
 
d
u
p
l
i
c
a
t
i
o
n
 
|
 
`
e
x
t
e
n
s
i
o
n
`
 
m
e
t
h
o
d
s
,
 
`
t
y
p
e
a
l
i
a
s
`
,
 
p
r
o
p
e
r
t
i
e
s
,
 
f
r
e
e
-
f
u
n
c
t
i
o
n
 
o
p
e
r
a
t
o
r
s
 
|


|
 
d
x
c
 
s
h
a
d
e
r
 
m
o
d
e
l
s
 
|
 
p
r
o
f
i
l
e
s
 
b
y
 
n
a
m
e
 
+
 
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
 
a
t
o
m
s
 
|




D
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
 
n
o
t
 
u
s
e
d
:
 
d
y
n
a
m
i
c
 
d
i
s
p
a
t
c
h
 
(
`
d
y
n
`
)
,
 
G
P
U
 
p
o
i
n
t
e
r
s
/
l
a
m
b
d
a
s
,
 
R
T
/
m
e
s
h


f
e
a
t
u
r
e
s
 
(
n
o
 
s
u
c
h
 
p
a
s
s
e
s
 
e
x
i
s
t
)
,
 
e
x
p
e
r
i
m
e
n
t
a
l
 
A
P
I
s
 
b
e
y
o
n
d
 
u
s
e
r
-
d
e
f
i
n
e
d


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
.




#
#
 
6
.
 
P
h
a
s
e
 
p
l
a
n




E
a
c
h
 
p
h
a
s
e
 
e
n
d
s
 
g
r
e
e
n
:
 
f
u
l
l
 
`
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
`
,
 
`
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
`
,
 
a
n
d
 
f
o
r
 
a
n
y
t
h
i
n
g


t
o
u
c
h
i
n
g
 
c
o
m
p
i
l
e
d
 
o
u
t
p
u
t
 
a
 
s
c
r
e
e
n
s
h
o
t
/
a
r
t
i
f
a
c
t
 
d
i
f
f
 
a
g
a
i
n
s
t
 
t
h
e
 
p
r
e
-
p
h
a
s
e


c
a
p
t
u
r
e
.




#
#
#
 
P
h
a
s
e
 
0
 
—
 
M
o
d
e
r
n
 
s
l
a
n
g
 
A
P
I
 
f
o
u
n
d
a
t
i
o
n




1
.
 
P
i
n
 
t
h
e
 
s
l
a
n
g
 
r
e
l
e
a
s
e
;
 
m
o
v
e
 
n
a
t
i
v
e
 
b
i
n
a
r
i
e
s
 
t
o


 
 
 
`
S
r
c
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
/
r
u
n
t
i
m
e
s
/
<
r
i
d
>
/
n
a
t
i
v
e
/
`
 
+
 
c
s
p
r
o
j
 
s
h
i
p
p
i
n
g
 
(
m
i
r
r
o
r
i
n
g


 
 
 
t
h
e
 
d
x
c
 
e
n
t
r
i
e
s
)
.


2
.
 
I
m
p
l
e
m
e
n
t
 
`
B
i
n
d
i
n
g
/
S
l
a
n
g
/
`
 
C
O
M
 
b
i
n
d
i
n
g
s
 
(
g
l
o
b
a
l
 
s
e
s
s
i
o
n
,
 
s
e
s
s
i
o
n
,
 
m
o
d
u
l
e
,


 
 
 
c
o
m
p
o
n
e
n
t
 
t
y
p
e
,
 
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
 
f
i
l
e
-
s
y
s
t
e
m
 
e
x
t
,
 
d
i
a
g
n
o
s
t
i
c
s
)
 
i
n
 
t
h
e
 
`
D
X
C
N
a
t
i
v
e
.
c
s
`


 
 
 
s
t
y
l
e
.


3
.
 
I
m
p
l
e
m
e
n
t
 
`
S
l
a
n
g
C
o
m
p
i
l
e
r
`
 
f
a
c
a
d
e
 
+
 
`
I
S
l
a
n
g
F
i
l
e
S
y
s
t
e
m
E
x
t
`
 
a
d
a
p
t
e
r
 
o
v
e
r
 
t
h
e


 
 
 
e
n
g
i
n
e
 
f
i
l
e
 
s
o
u
r
c
e
 
(
e
v
o
l
v
i
n
g
 
`
S
l
a
n
g
F
i
l
e
S
y
s
t
e
m
.
c
s
`
)
.


4
.
 
P
a
r
i
t
y
 
h
a
r
n
e
s
s
 
(
t
e
s
t
 
p
r
o
j
e
c
t
)
:
 
c
o
m
p
i
l
e
 
a
 
r
e
p
r
e
s
e
n
t
a
t
i
v
e
 
s
h
a
d
e
r
 
s
e
t


 
 
 
(
2
D
,
 
p
o
s
t
p
r
o
c
e
s
s
,
 
o
n
e
 
W
o
r
l
d
3
D
 
p
i
p
e
l
i
n
e
,
 
o
n
e
 
m
a
t
e
r
i
a
l
)
 
t
h
r
o
u
g
h
 
d
x
c
-
p
a
t
h
 
a
n
d


 
 
 
s
l
a
n
g
-
p
a
t
h
;
 
c
o
m
p
a
r
e
 
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
 
a
n
d
 
(
w
h
e
r
e
 
d
e
t
e
r
m
i
n
i
s
t
i
c
)
 
S
P
I
R
-
V
.
 
T
h
i
s
 
h
a
r
n
e
s
s


 
 
 
b
e
c
o
m
e
s
 
t
h
e
 
A
/
B
 
t
o
o
l
 
f
o
r
 
P
h
a
s
e
s
 
2
–
3
.




*
E
x
i
t
*
:
 
s
l
a
n
g
 
m
o
d
e
r
n
 
A
P
I
 
c
o
m
p
i
l
e
s
 
e
n
g
i
n
e
 
s
h
a
d
e
r
s
 
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
;
 
d
x
c
 
u
n
t
o
u
c
h
e
d
.




#
#
#
 
P
h
a
s
e
 
1
 
—
 
S
h
a
d
e
r
S
y
s
t
e
m
 
a
n
d
 
m
o
d
u
l
e
 
i
n
f
r
a
s
t
r
u
c
t
u
r
e




1
.
 
S
h
a
d
e
r
S
y
s
t
e
m
:
 
m
o
d
u
l
e
 
c
a
c
h
e
,
 
d
e
p
e
n
d
e
n
c
y
 
g
r
a
p
h
,
 
r
e
v
e
r
s
e
-
d
e
p
e
n
d
e
n
c
y
 
i
n
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
,


 
 
 
`
.
s
l
a
n
g
-
m
o
d
u
l
e
`
 
+
 
l
i
n
k
e
d
-
p
r
o
g
r
a
m
 
d
i
s
k
 
c
a
c
h
e
 
(
r
e
p
l
a
c
e
s
 
`
S
h
a
d
e
r
C
a
c
h
e
`
 
f
o
r
m
a
t
)
,


 
 
 
w
a
t
c
h
e
r
 
i
n
t
e
g
r
a
t
i
o
n
.


2
.
 
U
n
i
f
i
e
d
 
m
o
d
u
l
e
-
b
a
c
k
e
d
 
`
S
h
a
d
e
r
`
 
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
 
a
l
o
n
g
s
i
d
e
 
t
h
e
 
e
x
i
s
t
i
n
g
 
m
o
d
e
s


 
 
 
(
t
h
i
r
d
,
 
t
e
m
p
o
r
a
r
y
 
m
o
d
e
;
 
o
l
d
 
m
o
d
e
s
 
d
e
l
e
t
e
d
 
i
n
 
P
h
a
s
e
 
4
)
.


3
.
 
P
o
r
t
 
`
C
o
r
e
.
h
l
s
l
i
`
 
→
 
`
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
C
o
r
e
.
s
l
a
n
g
`
 
(
p
a
r
a
m
e
t
e
r
 
b
l
o
c
k
s
,
 
r
e
a
l


 
 
 
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
,
 
f
r
e
q
u
e
n
c
y
-
s
e
t
 
c
o
n
s
t
a
n
t
s
)
.
 
`
C
o
r
e
.
h
l
s
l
i
`
 
s
t
a
y
s
 
f
o
r
 
t
h
e
 
d
x
c
 
p
a
t
h
.


4
.
 
H
o
t
 
r
e
l
o
a
d
 
t
h
r
o
u
g
h
 
m
o
d
u
l
e
 
i
n
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
;
 
`
U
n
s
a
f
e
H
o
t
R
e
l
o
a
d
`
 
g
a
i
n
s
 
a
 
m
o
d
u
l
e
-
b
a
s
e
d


 
 
 
p
a
t
h
.




*
E
x
i
t
*
:
 
a
 
s
a
n
d
b
o
x
 
s
a
m
p
l
e
 
c
a
n
 
r
u
n
 
f
u
l
l
y
 
o
n
 
m
o
d
u
l
e
-
l
o
a
d
e
d
 
s
l
a
n
g
 
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
 
h
o
t


r
e
l
o
a
d
;
 
c
a
c
h
e
s
 
v
a
l
i
d
a
t
e
d
 
b
y
 
u
n
i
t
 
t
e
s
t
s
 
(
h
i
t
/
m
i
s
s
/
i
n
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
/
s
t
a
l
e
n
e
s
s
)
.




#
#
#
 
P
h
a
s
e
 
2
 
—
 
S
h
a
d
e
r
 
s
o
u
r
c
e
 
m
i
g
r
a
t
i
o
n
 
(
p
e
r
 
d
i
r
e
c
t
o
r
y
,
 
a
n
y
 
o
r
d
e
r
 
w
i
t
h
i
n
)




1
.
 
`
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
`
 
L
i
b
s
 
(
`
R
e
v
e
r
s
e
d
D
e
p
t
h
`
,
 
c
o
m
m
o
n
 
m
a
t
h
/
t
o
n
e
m
a
p
 
u
t
i
l
s
)
 
→
 
m
o
d
u
l
e
s
.


2
.
 
2
D
 
+
 
p
o
s
t
p
r
o
c
e
s
s
 
+
 
c
o
m
p
u
t
e
-
u
t
i
l
s
 
p
i
p
e
l
i
n
e
s
 
(
3
2
 
f
i
l
e
s
:
 
S
p
r
i
t
e
,
 
T
e
x
t
,
 
T
i
l
e
M
a
p
,


 
 
 
P
a
r
t
i
c
l
e
,
 
B
l
o
o
m
,
 
T
o
n
e
m
a
p
×
6
,
 
F
X
A
A
,
 
C
o
l
o
r
G
r
a
d
i
n
g
,
 
B
l
i
t
,
 
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
,
 
B
C
3
,


 
 
 
F
l
o
o
d
F
i
l
l
,
 
T
e
x
t
S
D
F
,
 
C
l
e
a
r
T
e
x
t
u
r
e
)
 
a
n
d
 
`
I
m
G
u
i
.
h
l
s
l
`
;
 
n
o
r
m
a
l
i
z
e
 
e
n
t
r
y
 
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
n
 
l
e
g
a
c
y
 
s
a
n
d
b
o
x
 
s
h
a
d
e
r
s
 
(
s
a
m
p
l
e
s
 
1
–
1
1
,
 
2
3
,
 
2
4
)
.


3
.
 
W
o
r
l
d
3
D
 
l
i
b
s
 
+
 
P
B
R
 
p
i
p
e
l
i
n
e
s
 
(
2
6
 
f
i
l
e
s
 
+
 
8
 
h
l
s
l
i
)
:
 
V
o
x
e
l
G
I
×
1
0
,
 
S
S
R
×
4
,
 
H
B
A
O
,


 
 
 
c
l
o
u
d
s
,
 
D
e
f
e
r
r
e
d
L
i
g
h
t
i
n
g
,
 
G
B
u
f
f
e
r
,
 
S
h
a
d
o
w
D
e
p
t
h
,
 
R
S
M
,
 
F
o
r
w
a
r
d
G
l
a
s
s
.
 
D
e
f
i
n
e


 
 
 
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
 
c
o
n
v
e
r
t
 
t
o
 
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
 
(
D
3
)
.


4
.
 
M
a
t
e
r
i
a
l
 
s
y
s
t
e
m
 
c
u
t
-
o
v
e
r
:
 
a
l
l
 
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
;
 
d
e
l
e
t
e
 
`
@
S
U
R
F
A
C
E
@
`


 
 
 
s
p
l
i
c
e
 
a
n
d
 
H
L
S
L
 
f
l
o
a
t
4
 
r
e
g
e
x
 
p
a
c
k
i
n
g
;
 
g
l
a
s
s
 
p
a
s
s
 
s
l
a
n
g
 
t
e
m
p
l
a
t
e
;


 
 
 
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
<
M
a
t
e
r
i
a
l
P
a
r
a
m
s
>
`
 
i
n
 
s
e
t
 
2
.


5
.
 
R
e
t
i
r
e
 
t
h
e
 
b
e
a
c
h
h
e
a
d
 
f
i
l
e
s
 
a
s
 
t
h
e
i
r
 
c
o
v
e
r
a
g
e
 
i
s
 
s
u
b
s
u
m
e
d
:


 
 
 
`
S
l
a
n
g
P
i
p
e
l
i
n
e
S
h
a
d
e
r
F
a
c
t
o
r
y
`
,
 
W
o
r
l
d
3
D
 
`
S
l
a
n
g
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
`
/
`
S
l
a
n
g
N
a
t
i
v
e
`


 
 
 
s
u
p
e
r
s
e
d
e
d
 
b
y
 
t
h
e
 
s
h
a
r
e
d
 
s
t
a
c
k
.




*
E
x
i
t
 
p
e
r
 
d
i
r
e
c
t
o
r
y
*
:
 
`
V
a
l
i
d
a
t
e
S
h
a
d
e
r
`
 
(
s
l
a
n
g
 
m
o
d
e
)
 
+
 
s
a
n
d
b
o
x
 
s
c
r
e
e
n
s
h
o
t
 
d
i
f
f
.


*
P
h
a
s
e
 
e
x
i
t
*
:
 
z
e
r
o
 
`
.
h
l
s
l
`
/
`
.
h
l
s
l
i
`
 
i
n
 
`
S
r
c
/
`
;
 
d
x
c
 
p
a
t
h
 
u
n
u
s
e
d
 
b
y
 
d
e
f
a
u
l
t
.




#
#
#
 
P
h
a
s
e
 
3
 
—
 
R
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
u
t
-
o
v
e
r
 
a
n
d
 
S
P
I
R
-
V
 
s
u
r
g
e
r
y
 
r
e
m
o
v
a
l




O
r
d
e
r
e
d
,
 
e
a
c
h
 
b
e
h
i
n
d
 
t
h
e
 
P
h
a
s
e
-
0
 
p
a
r
i
t
y
 
h
a
r
n
e
s
s
:




1
.
 
`
S
h
a
d
e
r
R
e
f
l
e
c
t
i
o
n
I
n
f
o
`
 
p
r
o
d
u
c
e
r
 
s
w
i
t
c
h
e
s
 
t
o
 
s
l
a
n
g
 
`
P
r
o
g
r
a
m
L
a
y
o
u
t
`
 
(
b
i
n
d
i
n
g


 
 
 
r
a
n
g
e
s
 
A
P
I
;
 
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
 
v
a
r
y
i
n
g
 
I
/
O
;
 
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
;
 
t
h
r
e
a
d
 
g
r
o
u
p
 
s
i
z
e
 
v
i
a


 
 
 
`
E
n
t
r
y
P
o
i
n
t
R
e
f
l
e
c
t
i
o
n
`
)
.
 
`
S
p
i
r
v
R
e
f
l
e
c
t
o
r
`
 
b
e
c
o
m
e
s
 
a
 
c
r
o
s
s
-
c
h
e
c
k
 
i
n
 
t
e
s
t
s
.


2
.
 
D
e
l
e
t
e
 
`
S
l
a
n
g
B
i
n
d
i
n
g
R
e
m
a
p
p
e
r
`
 
—
 
b
i
n
d
i
n
g
s
 
n
o
w
 
e
x
p
r
e
s
s
e
d
 
i
n
 
s
o
u
r
c
e
 
(
D
2
)
;


 
 
 
d
e
t
e
r
m
i
n
i
s
m
 
o
f
 
i
n
-
s
e
t
 
a
s
s
i
g
n
m
e
n
t
 
p
i
n
n
e
d
 
b
y
 
t
e
s
t
s
.


3
.
 
D
e
l
e
t
e
 
`
S
l
a
n
g
S
p
i
r
v
F
a
c
t
s
`
 
(
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
 
n
o
w
 
c
o
v
e
r
s
 
t
h
r
e
a
d
 
g
r
o
u
p
 
s
i
z
e
;
 
s
t
o
r
a
g
e


 
 
 
f
o
r
m
a
t
s
 
v
i
a
 
t
y
p
e
-
l
a
y
o
u
t
 
r
e
s
o
u
r
c
e
 
s
h
a
p
e
/
a
c
c
e
s
s
 
—
 
v
e
r
i
f
y
)
.


4
.
 
V
e
r
i
f
y
 
a
n
d
 
d
e
l
e
t
e
 
w
o
r
k
a
r
o
u
n
d
s
 
o
n
e
 
b
y
 
o
n
e
 
a
g
a
i
n
s
t
 
t
h
e
 
p
i
n
n
e
d
 
s
l
a
n
g
 
v
e
r
s
i
o
n
:


 
 
 
`
S
l
a
n
g
B
a
s
e
I
n
s
t
a
n
c
e
Z
e
r
o
e
r
`
,
 
D
r
a
w
P
a
r
a
m
e
t
e
r
s
-
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
t
r
i
p
p
i
n
g
,


 
 
 
`
-
e
m
i
t
-
s
p
i
r
v
-
v
i
a
-
g
l
s
l
`
 
(
i
n
c
l
u
d
i
n
g
 
t
h
e
 
`
S
c
r
e
e
n
S
p
a
c
e
R
e
f
l
e
c
t
i
o
n
B
l
u
e
N
o
i
s
e
`


 
 
 
e
x
c
e
p
t
i
o
n
)
.
 
N
o
 
c
o
m
p
a
t
i
b
i
l
i
t
y
 
b
a
c
k
e
n
d
 
r
e
m
a
i
n
s
;
 
d
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


5
.
 
D
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
s
:
 
v
e
r
i
f
y
 
s
l
a
n
g
 
e
m
i
t
s
 
a
 
n
a
g
a
-
a
c
c
e
p
t
e
d
 
`
D
e
p
t
h
`
 
o
p
e
r
a
n
d
 
f
o
r
 
t
h
e


 
 
 
e
n
g
i
n
e
'
s
 
d
e
p
t
h
-
t
e
x
t
u
r
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
;
 
i
f
 
n
o
t
 
u
n
i
v
e
r
s
a
l
l
y
,
 
k
e
e
p
 
a
 
m
i
n
i
m
a
l


 
 
 
p
a
t
c
h
e
r
 
d
r
i
v
e
n
 
b
y
 
s
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
/
`
[
A
l
c
o
D
e
p
t
h
]
`
 
—
 
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
e
s
 
a
r
e
 
d
e
l
e
t
e
d


 
 
 
e
i
t
h
e
r
 
w
a
y
.


6
.
 
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
 
s
a
m
p
l
e
r
s
:
 
r
e
f
l
e
c
t
 
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
 
d
i
r
e
c
t
l
y
;
 
d
e
l
e
t
e


 
 
 
`
M
a
r
k
D
e
p
t
h
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
a
m
p
l
e
r
s
`
 
a
n
d
 
t
h
e
 
`
S
a
m
p
l
e
r
S
u
f
f
i
x
`
 
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
.


7
.
 
C
o
u
n
t
e
r
s
:
 
r
e
-
d
e
r
i
v
e
 
o
w
n
e
r
 
p
a
i
r
i
n
g
 
f
r
o
m
 
s
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
;
 
d
e
l
e
t
e
 
t
h
e


 
 
 
`
c
o
u
n
t
e
r
.
v
a
r
.
`
 
/
 
`
_
c
o
u
n
t
e
r
`
 
n
a
m
e
 
l
o
g
i
c
 
a
n
d
 
t
h
e
 
b
i
n
d
i
n
g
-
a
d
j
a
c
e
n
c
y
 
f
a
l
l
b
a
c
k
.




*
E
x
i
t
*
:
 
n
o
 
S
P
I
R
-
V
 
b
i
n
a
r
y
 
r
e
w
r
i
t
i
n
g
 
o
n
 
a
n
y
 
s
h
a
d
e
r
;
 
`
S
p
i
r
v
R
e
f
l
e
c
t
o
r
`
 
o
n
l
y
 
i
n


t
e
s
t
s
;
 
a
l
l
 
r
e
n
d
e
r
i
n
g
 
s
a
n
d
b
o
x
e
s
 
s
c
r
e
e
n
s
h
o
t
-
c
l
e
a
n
.




#
#
#
 
P
h
a
s
e
 
4
 
—
 
T
e
a
r
d
o
w
n




1
.
 
D
e
l
e
t
e
 
d
x
c
:
 
`
B
i
n
d
i
n
g
/
D
x
c
/
`
,
 
`
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
D
x
c
`
,
 
d
x
c
/
d
x
i
l
 
b
i
n
a
r
i
e
s
,


 
 
 
`
F
i
l
e
E
x
t
.
c
s
`
 
d
x
c
-
o
n
l
y
 
e
n
t
r
i
e
s
.


2
.
 
D
e
l
e
t
e
 
`
I
n
c
l
u
d
e
H
e
l
p
e
r
`
,
 
`
A
s
s
e
t
L
o
a
d
e
r
S
h
a
d
e
r
H
L
S
L
(
I
n
c
l
u
d
e
)
`
,
 
t
e
x
t
-
m
o
d
e
 
`
S
h
a
d
e
r
`


 
 
 
c
t
o
r
,
 
p
r
o
v
i
d
e
r
 
c
t
o
r
,
 
`
U
n
s
a
f
e
H
o
t
R
e
l
o
a
d
(
t
e
x
t
)
`
,
 
r
e
g
e
x
 
e
n
t
r
y
 
d
i
s
c
o
v
e
r
y
,


 
 
 
`
S
p
i
r
v
R
e
f
l
e
c
t
o
r
`
 
(
p
o
s
t
 
c
r
o
s
s
-
c
h
e
c
k
)
,
 
`
S
p
i
r
v
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
P
a
t
c
h
e
r
`
 
r
e
m
n
a
n
t
s
.


3
.
 
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
 
g
e
n
e
r
a
t
o
r
 
e
m
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
s
;
 
`
G
a
m
e
E
n
g
i
n
e
.
L
o
a
d
e
r
.
c
s
`
 
d
r
o
p
s


 
 
 
s
h
a
d
e
r
 
l
o
a
d
e
r
s
.


4
.
 
D
o
c
s
:
 
u
p
d
a
t
e
 
`
S
h
a
d
e
r
_
B
i
n
d
i
n
g
_
S
l
o
t
_
C
o
l
l
i
s
i
o
n
s
.
m
d
`
 
(
b
i
n
d
i
n
g
 
s
e
m
a
n
t
i
c
s
 
n
o
w


 
 
 
s
l
a
n
g
-
d
e
f
i
n
e
d
)
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
B
i
n
d
G
r
o
u
p
R
e
f
a
c
t
o
r
P
l
a
n
.
m
d
`
 
(
§
8
 
n
o
t
e
:
 
b
i
n
d
l
e
s
s
 
f
u
t
u
r
e


 
 
 
v
i
a
 
`
D
e
s
c
r
i
p
t
o
r
H
a
n
d
l
e
<
T
>
`
/
`
R
e
s
o
u
r
c
e
D
e
s
c
r
i
p
t
o
r
H
e
a
p
`
)
,
 
a
d
d
 
a
 
s
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
 
(
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
,
 
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
-
v
s
-
d
e
f
i
n
e
 
p
o
l
i
c
y
)
.




#
#
 
7
.
 
T
e
s
t
 
a
n
d
 
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
 
p
l
a
n




-
 
`
V
a
l
i
d
a
t
e
S
h
a
d
e
r
`
 
b
e
c
o
m
e
s
 
s
l
a
n
g
-
b
a
s
e
d
:
 
e
v
e
r
y
 
m
o
d
u
l
e
 
×
 
e
v
e
r
y
 
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


 
 
c
o
m
p
i
l
e
s
 
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
;
 
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
v
e
n
t
i
o
n
s
 
v
a
l
i
d
a
t
e
d
 
(
n
a
m
i
n
g
,
 
s
e
t
 
u
s
a
g
e
,


 
 
b
u
d
g
e
t
 
l
i
m
i
t
s
 
p
e
r
 
w
g
p
u
 
d
e
f
a
u
l
t
s
)
.


-
 
N
e
w
 
u
n
i
t
 
t
e
s
t
s
:
 
S
h
a
d
e
r
S
y
s
t
e
m
 
c
a
c
h
e
 
(
h
i
t
/
m
i
s
s
,
 
d
e
p
e
n
d
e
n
c
y
 
i
n
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
,


 
 
`
.
s
l
a
n
g
-
m
o
d
u
l
e
`
 
s
t
a
l
e
n
e
s
s
)
,
 
s
l
a
n
g
→
`
S
h
a
d
e
r
R
e
f
l
e
c
t
i
o
n
I
n
f
o
`
 
m
a
p
p
i
n
g
 
(
p
a
c
k
e
d


 
 
g
r
o
u
p
s
,
 
s
a
m
p
l
e
r
 
k
i
n
d
s
,
 
c
o
u
n
t
e
r
s
,
 
v
e
r
t
e
x
 
i
n
p
u
t
s
,
 
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
)
,
 
b
i
n
d
i
n
g


 
 
d
e
t
e
r
m
i
n
i
s
m
 
a
c
r
o
s
s
 
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
.


-
 
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
 
t
e
s
t
s
:
 
e
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
 
s
t
a
r
t
s
 
w
i
t
h
 
`
m
o
d
u
l
e
`
 
+
 
l
a
n
g
u
a
g
e
 
p
i
n
;
 
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
 
p
o
i
n
t
 
h
a
s
 
`
[
s
h
a
d
e
r
(
.
.
.
)
]
`
;
 
n
o
 
`
r
e
g
i
s
t
e
r
`
 
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
;
 
n
o
 
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
n
 
`
S
r
c
/
`
.


-
 
A
/
B
 
w
h
i
l
e
 
d
x
c
 
e
x
i
s
t
s
:
 
p
a
r
i
t
y
 
h
a
r
n
e
s
s
 
+
 
s
c
r
e
e
n
s
h
o
t
 
d
i
f
f
s
 
(
d
e
f
e
r
r
e
d
 
P
B
R
 
s
a
n
d
b
o
x
,


 
 
v
o
x
e
l
 
G
I
 
s
a
n
d
b
o
x
,
 
2
D
/
C
a
n
v
a
s
 
s
a
n
d
b
o
x
,
 
b
o
o
t
 
s
c
r
e
e
n
s
h
o
t
)
 
a
g
a
i
n
s
t
 
p
r
e
-
p
h
a
s
e


 
 
c
a
p
t
u
r
e
s
,
 
p
e
r
 
t
h
e
 
e
s
t
a
b
l
i
s
h
e
d
 
a
r
t
i
f
a
c
t
s
 
w
o
r
k
f
l
o
w
.


-
 
O
p
t
i
o
n
a
l
 
C
I
 
h
a
r
d
e
n
i
n
g
:
 
`
s
l
a
n
g
c
 
-
d
e
p
f
i
l
e
`
 
f
o
r
 
o
f
f
l
i
n
e
 
d
e
p
e
n
d
e
n
c
y
 
c
h
e
c
k
i
n
g
 
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
 
g
r
a
p
h
.




#
#
 
8
.
 
R
i
s
k
s
 
a
n
d
 
m
i
t
i
g
a
t
i
o
n
s




-
 
*
*
s
l
a
n
g
 
S
P
I
R
-
V
 
v
s
 
n
a
g
a
/
w
g
p
u
 
g
a
p
s
*
*
 
(
p
r
e
v
i
o
u
s
l
y
 
o
b
s
e
r
v
e
d
:
 
`
B
a
s
e
I
n
s
t
a
n
c
e
`
 
a
n
d


 
 
`
D
r
a
w
P
a
r
a
m
e
t
e
r
s
`
)
.
 
M
i
t
i
g
a
t
i
o
n
:
 
p
i
n
n
e
d
 
v
e
r
s
i
o
n
 
a
n
d
 
d
i
r
e
c
t
-
o
u
t
p
u
t
 
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
;


 
 
d
o
 
n
o
t
 
a
d
d
 
a
 
s
e
c
o
n
d
 
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
 
a
s
 
a
 
w
o
r
k
a
r
o
u
n
d
.


-
 
*
*
D
e
p
t
h
-
t
e
x
t
u
r
e
 
`
D
e
p
t
h
`
 
o
p
e
r
a
n
d
*
*
.
 
M
i
t
i
g
a
t
i
o
n
:
 
d
e
d
i
c
a
t
e
d
 
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
 
t
e
s
t
s
 
p
r
o
v
e


 
 
t
h
a
t
 
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
 
m
a
p
s
 
t
o
 
W
e
b
G
P
U
'
s
 
d
e
p
t
h
 
s
a
m
p
l
e
 
t
y
p
e
;
 
t
h
e
 
r
e
n
d
e
r
e
r
 
b
i
n
d
s


 
 
t
h
e
 
r
e
a
l
 
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
,
 
w
i
t
h
 
n
o
 
m
i
r
r
o
r
 
o
r
 
b
i
n
a
r
y
 
p
a
t
c
h
e
r
.


-
 
*
*
N
a
g
a
 
S
P
I
R
-
V
 
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
 
o
n
 
V
u
l
k
a
n
*
*
.
 
V
a
l
i
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
 
c
o
n
t
a
i
n
i
n
g


 
 
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
 
l
o
a
d
s
 
a
n
d
 
o
r
d
i
n
a
r
y
 
l
o
o
p
 
c
o
n
t
r
o
l
 
f
l
o
w
 
c
a
u
s
e
d
 
d
e
v
i
c
e
 
l
o
s
s
 
o
n
l
y


 
 
a
f
t
e
r
 
t
h
e
 
N
a
g
a
 
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
 
M
i
t
i
g
a
t
i
o
n
:
 
r
e
q
u
e
s
t
 
w
g
p
u
-
c
o
r
e
'
s
 
e
x
i
s
t
i
n
g


 
 
`
P
A
S
S
T
H
R
O
U
G
H
_
S
H
A
D
E
R
S
`
 
f
e
a
t
u
r
e
 
a
n
d
 
s
u
b
m
i
t
 
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
P
I
R
-
V
 
d
i
r
e
c
t
l
y
 
t
o
 
V
u
l
k
a
n
;


 
 
t
h
e
 
C
 
A
P
I
 
e
x
p
o
s
u
r
e
 
i
s
 
a
 
p
i
n
n
e
d
,
 
r
e
p
r
o
d
u
c
i
b
l
e
 
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
 
p
a
t
c
h
 
r
a
t
h
e
r
 
t
h
a
n
 
a


 
 
s
h
a
d
e
r
 
r
e
w
r
i
t
e
 
o
r
 
a
l
t
e
r
n
a
t
e
 
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
.


-
 
*
*
V
e
r
t
e
x
 
i
n
p
u
t
 
l
a
y
o
u
t
 
d
r
i
f
t
*
*
 
(
s
e
m
a
n
t
i
c
s
 
v
s
 
t
h
e
 
c
u
r
r
e
n
t
 
L
o
c
a
t
i
o
n
-
s
c
a
n
 
p
a
c
k
i
n
g
)
.


 
 
M
i
t
i
g
a
t
i
o
n
:
 
p
a
r
i
t
y
 
h
a
r
n
e
s
s
 
c
o
m
p
a
r
e
s
 
`
V
e
r
t
e
x
I
n
p
u
t
L
a
y
o
u
t
`
 
f
o
r
 
e
v
e
r
y
 
m
i
g
r
a
t
e
d


 
 
s
h
a
d
e
r
 
b
e
f
o
r
e
 
c
u
t
-
o
v
e
r
.


-
 
*
*
S
e
s
s
i
o
n
-
g
l
o
b
a
l
 
m
a
c
r
o
s
 
v
s
 
d
e
f
i
n
e
 
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
 
m
i
d
-
t
r
a
n
s
i
t
i
o
n
*
*
.
 
M
i
t
i
g
a
t
i
o
n
:


 
 
P
h
a
s
e
 
2
 
c
o
n
v
e
r
t
s
 
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
 
t
o
 
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
b
e
f
o
r
e
*
 
t
h
o
s
e
 
s
h
a
d
e
r
s
 
m
o
v
e
;


 
 
i
n
t
e
r
i
m
 
d
e
f
i
n
e
 
s
e
t
s
 
u
s
e
 
d
e
d
i
c
a
t
e
d
 
s
e
s
s
i
o
n
s
 
(
a
c
c
e
p
t
e
d
 
c
o
s
t
,
 
b
o
u
n
d
e
d
 
l
i
f
e
t
i
m
e
)
.


-
 
*
*
B
i
n
a
r
y
 
m
o
d
u
l
e
 
c
a
c
h
e
 
s
t
a
l
e
n
e
s
s
*
*
 
(
p
r
i
m
a
r
y
 
s
o
u
r
c
e
 
a
b
s
e
n
t
 
→
 
a
c
c
e
p
t
e
d
 
a
s


 
 
u
p
-
t
o
-
d
a
t
e
)
.
 
M
i
t
i
g
a
t
i
o
n
:
 
e
x
p
l
i
c
i
t
 
(
s
l
a
n
g
 
v
e
r
s
i
o
n
 
+
 
o
p
t
i
o
n
s
)
 
s
t
a
m
p
 
i
n
 
o
u
r
 
c
a
c
h
e


 
 
k
e
y
s
;
 
s
h
i
p
p
e
d
 
b
u
i
l
d
s
 
k
e
e
p
 
s
o
u
r
c
e
s
 
o
r
 
s
t
a
m
p
.


-
 
*
*
C
o
u
n
t
e
r
 
p
a
i
r
i
n
g
 
r
e
g
r
e
s
s
i
o
n
*
*
 
(
t
h
e
 
`
c
o
u
n
t
e
r
.
v
a
r
.
`
 
i
n
c
i
d
e
n
t
 
c
l
a
s
s
)
.


 
 
M
i
t
i
g
a
t
i
o
n
:
 
d
e
d
i
c
a
t
e
d
 
t
e
s
t
s
 
m
i
r
r
o
r
i
n
g
 
`
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
W
i
t
h
C
o
l
o
r
G
r
a
d
i
n
g
`
 
a
n
d
 
t
h
e


 
 
c
o
m
p
u
t
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
 
c
o
u
n
t
e
r
 
s
h
a
p
e
s
 
b
e
f
o
r
e
 
t
h
e
 
n
a
m
e
 
l
o
g
i
c
 
i
s
 
d
e
l
e
t
e
d
.


-
 
*
*
C
o
m
p
i
l
e
-
t
i
m
e
 
r
e
g
r
e
s
s
i
o
n
*
*
.
 
M
i
t
i
g
a
t
i
o
n
:
 
m
o
d
u
l
e
 
c
a
c
h
e
 
+
 
I
R
 
b
l
o
b
s
 
a
r
e
 
e
x
p
e
c
t
e
d


 
 
t
o
 
*
i
m
p
r
o
v
e
*
 
o
v
e
r
 
w
h
o
l
e
-
T
U
 
r
e
c
o
m
p
i
l
e
s
;
 
m
e
a
s
u
r
e
 
b
o
o
t
 
c
o
m
p
i
l
e
 
t
i
m
e
 
b
e
f
o
r
e
/
a
f
t
e
r


 
 
P
h
a
s
e
 
1
 
a
n
d
 
k
e
e
p
 
t
h
e
 
n
u
m
b
e
r
s
 
i
n
 
t
h
e
 
p
h
a
s
e
 
n
o
t
e
s
.




#
#
 
9
.
 
O
u
t
 
o
f
 
s
c
o
p
e
 
/
 
f
u
t
u
r
e
 
d
i
r
e
c
t
i
o
n
s




-
 
W
G
S
L
 
o
u
t
p
u
t
 
f
o
r
 
a
 
D
a
w
n
/
w
e
b
 
b
u
i
l
d
 
(
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
y
s
t
e
m
 
+
 
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
 
m
a
p
p
i
n
g


 
 
a
l
r
e
a
d
y
 
k
e
e
p
 
t
h
e
 
d
o
o
r
 
o
p
e
n
)
.


-
 
B
i
n
d
l
e
s
s
 
m
a
t
e
r
i
a
l
s
 
v
i
a
 
`
D
e
s
c
r
i
p
t
o
r
H
a
n
d
l
e
<
T
>
`
 
+
 
`
R
e
s
o
u
r
c
e
D
e
s
c
r
i
p
t
o
r
H
e
a
p
[
]
`
.


-
 
S
l
a
n
g
 
l
a
n
g
u
a
g
e
 
v
e
r
s
i
o
n
 
2
0
2
6
 
a
d
o
p
t
i
o
n
 
(
`
d
y
n
`
,
 
t
u
p
l
e
 
c
h
a
n
g
e
s
)
 
a
f
t
e
r
 
s
t
a
b
i
l
i
z
a
t
i
o
n
.


-
 
P
r
e
c
o
m
p
i
l
e
d
 
s
h
a
d
e
r
 
s
h
i
p
p
i
n
g
 
(
o
f
f
l
i
n
e
 
`
s
l
a
n
g
c
`
 
p
i
p
e
l
i
n
e
 
p
r
o
d
u
c
i
n
g


 
 
`
.
s
l
a
n
g
-
m
o
d
u
l
e
`
/
l
i
n
k
e
d
 
b
i
n
a
r
i
e
s
;
 
r
u
n
t
i
m
e
 
J
I
T
 
p
a
t
h
 
s
t
a
y
s
 
f
o
r
 
t
h
e
 
e
d
i
t
o
r
)
.


-
 
O
b
f
u
s
c
a
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
e
r
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
 
f
o
r
 
s
h
i
p
p
e
d
 
b
u
i
l
d
s
.

